using System.Globalization;
using TalkPro.Core.Model;
using TalkPro.Core.Parsing;
using TalkPro.Ingestion.Formats.Synthetic;
using TalkPro.Ingestion.Parsing;
using TalkPro.Ingestion.Speakers;
using TalkPro.Memory;
using TalkPro.Persona.Analysis;
using TalkPro.Privacy;

namespace TalkPro.Web.Viewer;

public enum ViewerError
{
    None,
    Empty,
    TooLarge,
    UnsupportedEncoding,
    UnknownFormat,
    AmbiguousFormat,
    NotAttested,
    UnknownOwner,
    OwnerHasNoMessages,

    /// <summary>Possible same-name or renamed participants were not confirmed by the user.</summary>
    SuspicionsOpen,
}

/// <summary>A participant to choose as "나". Display name is personal data, kept in page memory only.</summary>
public sealed record ViewerParticipant(ParticipantId Id, string Name, int Messages)
{
    public override string ToString() => $"ViewerParticipant {{ Id = {Id}, Messages = {Messages} }}";
}

public sealed record ViewerLine(string Time, string Speaker, string Text, bool IsOwner)
{
    public override string ToString() => $"ViewerLine {{ Owner = {IsOwner} }}";
}

/// <summary>A search result: the stored record (LocalSearch view), never generated text.</summary>
public sealed record ViewerCard(string Date, IReadOnlyList<ViewerLine> Lines)
{
    public override string ToString() => $"ViewerCard {{ Lines = {Lines.Count} }}";
}

/// <summary>
/// The web viewer's whole pipeline, in memory (DD-026): parse → speaker review (owner) → privacy scan with the
/// conservative default decisions → owner-centred analysis on the Analysis view → keyword search on the LocalSearch
/// view. Nothing is stored, uploaded or logged; closing or reloading the page discards everything.
/// Reads the validated real format (KakaoTalk PC export, DD-027) and the synthetic test formats.
/// </summary>
public sealed class ViewerSession
{
    public const int MaxBytes = 50 * 1024 * 1024;

    private readonly ChatExportParser _parser = new([.. TalkPro.Ingestion.Formats.Kakao.KakaoFormats.All, .. SyntheticFormats.All]);
    private readonly PrivacyScanner _scanner = PrivacyScanner.CreateDefault();
    private ParsedChat? _parsed;
    private Prepared? _prepared;

    public IReadOnlyList<ViewerParticipant> Participants { get; private set; } = [];

    /// <summary>Speaker hints the user must confirm (merge/split are available in the PC app only).</summary>
    public IReadOnlyList<string> Suspicions { get; private set; } = [];

    public string? FileSummary { get; private set; }

    public CommunicationReport? Report { get; private set; }

    public PrivacySummary? Privacy { get; private set; }

    public bool IsReady => _prepared is not null;

    public ViewerError Load(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Reset();
        if (content.Length > MaxBytes)
        {
            return ViewerError.TooLarge;
        }

        var outcome = _parser.Parse(content, ParseContext.ForLocalTimeZone());
        if (!outcome.IsSuccess)
        {
            return outcome.Failure switch
            {
                ParseFailure.Empty => ViewerError.Empty,
                ParseFailure.TooLarge => ViewerError.TooLarge,
                ParseFailure.UnsupportedEncoding => ViewerError.UnsupportedEncoding,
                ParseFailure.AmbiguousFormat => ViewerError.AmbiguousFormat,
                _ => ViewerError.UnknownFormat,
            };
        }

        _parsed = outcome.Chat!;
        var counts = _parsed.Messages.Where(m => m.SpeakerId is not null).GroupBy(m => m.SpeakerId!.Value).ToDictionary(g => g.Key, g => g.Count());
        Participants = [.. _parsed.Participants.Select(p => new ViewerParticipant(p.Id, p.DisplayName, counts.GetValueOrDefault(p.Id)))];
        FileSummary = string.Create(CultureInfo.GetCultureInfo("ko-KR"), $"메시지 {_parsed.Messages.Count:N0}개 · 참여자 {_parsed.Participants.Count}명 · 인코딩 {_parsed.Encoding}");
        var names = _parsed.Participants.ToDictionary(p => p.Id, p => p.DisplayName);
        Suspicions = [.. SpeakerReview.Start(_parsed).Suspicions.Where(x => x.Status == SuspicionStatus.Open).Select(x => Describe(x, names))];
        return ViewerError.None;
    }

    /// <summary>Confirms the owner, applies the privacy defaults and computes the analysis.</summary>
    public ViewerError Confirm(ParticipantId owner, bool attested, bool suspicionsConfirmed)
    {
        if (_parsed is null)
        {
            return ViewerError.UnknownFormat;
        }

        if (!attested)
        {
            return ViewerError.NotAttested;
        }

        var start = SpeakerReview.Start(_parsed).ConfirmAttestation();
        if (start.Participants.All(p => p.Id != owner))
        {
            return ViewerError.UnknownOwner;
        }

        var step = start.SelectOwner(owner);
        if (step.Error != ReviewError.None)
        {
            return ViewerError.OwnerHasNoMessages;
        }

        var review0 = step.Review;
        if (review0.Suspicions.Any(x => x.Status == SuspicionStatus.Open))
        {
            if (!suspicionsConfirmed)
            {
                return ViewerError.SuspicionsOpen;
            }

            foreach (var open in review0.Suspicions.Where(x => x.Status == SuspicionStatus.Open).ToList())
            {
                review0 = review0.Acknowledge(open.Id).Review;
            }
        }

        if (review0.Complete().Chat is not { } chat)
        {
            return ViewerError.OwnerHasNoMessages;
        }

        var findings = _scanner.Scan(chat.Messages, chat.Participants);
        var review = PrivacyReview.Start(findings, chat.Messages.Count);
        var import = PrivacyImportBuilder.Build(chat, review);
        var decisions = import.Findings.Select(f => (f, import.Decisions[f.Id])).ToList();
        var analysis = PurposeRedactor.RenderAll(import.Chat.Messages, decisions, import.ExcludedMessages, RedactionPurpose.Analysis);
        var sessions = new ConversationSessionizer().Split(analysis);
        Report = CommunicationAnalyzer.Analyze(owner, import.Chat.Participants, analysis, sessions);
        Privacy = review.Summary();

        var search = PurposeRedactor.RenderAll(import.Chat.Messages, decisions, import.ExcludedMessages, RedactionPurpose.LocalSearch);
        var searchSessions = new ConversationSessionizer().Split(search);
        _prepared = new Prepared(
            owner,
            import.Chat.Participants.ToDictionary(p => p.Id, p => p.DisplayName),
            search.ToDictionary(m => m.Id),
            MemoryChunker.Build(owner, search, searchSessions),
            search.Count == 0 ? DateTimeOffset.UnixEpoch : search.Max(m => m.Timestamp));
        return ViewerError.None;
    }

    /// <summary>Keyword search over the LocalSearch view (no embeddings in the browser). Empty = "기록에서 찾지 못했습니다."</summary>
    public IReadOnlyList<ViewerCard> Search(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (_prepared is not { } data)
        {
            return [];
        }

        var terms = KoreanQuery.Terms(query.Trim());
        var sparse = new Dictionary<MessageId, double>();
        foreach (var (id, message) in data.View)
        {
            var hits = terms.Count(t => message.Text.Contains(t, StringComparison.OrdinalIgnoreCase));
            if (hits > 0)
            {
                sparse[id] = hits;
            }
        }

        var korean = CultureInfo.GetCultureInfo("ko-KR");
        return [.. HybridRetriever.Rank(data.Items, new Dictionary<MemoryItemId, double>(), sparse, data.Latest, _ => false, new RetrievalWeights { Dense = 0 })
            .Select(r => new ViewerCard(
                r.Item.Start.ToString("yyyy-MM-dd (ddd)", korean),
                [.. r.Item.SourceMessageIds.Where(data.View.ContainsKey).Select(id => data.View[id]).Select(m => new ViewerLine(
                    m.Timestamp.ToString("HH:mm", korean),
                    m.SpeakerId is { } s && data.Names.TryGetValue(s, out var name) ? name : "-",
                    m.Text,
                    m.SpeakerId == data.Owner))]))];
    }

    public void Reset()
    {
        _parsed = null;
        _prepared = null;
        Participants = [];
        Suspicions = [];
        FileSummary = null;
        Report = null;
        Privacy = null;
    }

    public override string ToString() => $"ViewerSession {{ Ready = {IsReady} }}";

    private static string Describe(SpeakerSuspicion suspicion, Dictionary<ParticipantId, string> names)
    {
        var who = string.Join(" / ", suspicion.Participants.Select(p => names.GetValueOrDefault(p, "-")));
        return suspicion.Reason switch
        {
            SuspicionReason.NormalizedNameMatch => $"이름이 거의 같습니다: {who}",
            SuspicionReason.NameContainment => $"한 이름이 다른 이름을 포함합니다: {who}",
            SuspicionReason.LongInactivityGap => $"{who}: 오랫동안 말이 없다가 다시 등장합니다",
            SuspicionReason.LeftAndReturned => $"{who}: 나간 뒤 다시 대화합니다",
            _ => who,
        };
    }

    private sealed record Prepared(
        ParticipantId Owner,
        IReadOnlyDictionary<ParticipantId, string> Names,
        IReadOnlyDictionary<MessageId, ChatMessage> View,
        IReadOnlyList<MemoryItem> Items,
        DateTimeOffset Latest)
    {
        public override string ToString() => $"Prepared {{ Items = {Items.Count} }}";
    }
}
