using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using TalkPro.Core.Model;
using TalkPro.Core.Parsing;

namespace TalkPro.Ingestion.Parsing;

public enum ParseFailure
{
    None,
    Empty,
    TooLarge,
    UnsupportedEncoding,
    UnknownFormat,
    AmbiguousFormat,
}

public sealed record ChatParseOutcome(ParseFailure Failure, ParsedChat? Chat)
{
    public bool IsSuccess => Failure == ParseFailure.None && Chat is not null;

    public override string ToString() => $"ChatParseOutcome {{ Failure = {Failure}, Chat = {Chat} }}";
}

/// <summary>
/// Ingestion driver: decode → split lines → select adapter by content → feed lines → resolve speakers.
/// Owns line numbering and the format-independent guards (line length, NUL, regex timeout).
/// Never logs or exposes line content.
/// </summary>
public sealed class ChatExportParser(IEnumerable<IChatFormatAdapter> adapters)
{
    public const long MaxFileBytes = 512L * 1024 * 1024;
    public const int DetectionSampleLines = 64;
    public const int DetectionSampleLineLength = 4_096;
    public const double MinimumDetectionScore = 0.5;
    public const double AmbiguityMargin = 0.1;

    private readonly IReadOnlyList<IChatFormatAdapter> _adapters = [.. adapters];

    public ChatParseOutcome Parse(ReadOnlySpan<byte> content, ParseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (content.Length > MaxFileBytes)
        {
            return Fail(ParseFailure.TooLarge);
        }

        if (!TextDecoding.TryDecode(content, out var decoded))
        {
            return Fail(ParseFailure.UnsupportedEncoding);
        }

        if (decoded.Text.Length == 0)
        {
            return Fail(ParseFailure.Empty);
        }

        var lines = LineSplitter.Split(decoded.Text);
        var (adapter, failure) = SelectAdapter(lines);
        return adapter is null ? Fail(failure) : new ChatParseOutcome(ParseFailure.None, Run(adapter, lines, decoded.Encoding, context));
    }

    /// <summary>Picks the adapter with the highest score; refuses weak or near-tied matches.</summary>
    public (IChatFormatAdapter? Adapter, ParseFailure Failure) SelectAdapter(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var sample = lines
            .Take(DetectionSampleLines)
            .Select(l => l.Length > DetectionSampleLineLength ? l[..DetectionSampleLineLength] : l)
            .ToList();

        if (sample.TrueForAll(string.IsNullOrWhiteSpace))
        {
            return (null, ParseFailure.UnknownFormat);
        }

        var ranked = _adapters
            .Select(a => (Adapter: a, Score: SafeDetect(a, sample)))
            .OrderByDescending(x => x.Score)
            .ToList();

        if (ranked.Count == 0 || ranked[0].Score < MinimumDetectionScore)
        {
            return (null, ParseFailure.UnknownFormat);
        }

        if (ranked.Count > 1 && ranked[0].Score - ranked[1].Score < AmbiguityMargin)
        {
            return (null, ParseFailure.AmbiguousFormat);
        }

        return (ranked[0].Adapter, ParseFailure.None);
    }

    private static double SafeDetect(IChatFormatAdapter adapter, List<string> sample)
    {
        try
        {
            var score = adapter.Detect(sample);
            return double.IsFinite(score) ? Math.Clamp(score, 0, 1) : 0;
        }
        catch (RegexMatchTimeoutException)
        {
            return 0;
        }
    }

    private static ParsedChat Run(IChatFormatAdapter adapter, List<string> lines, SourceEncoding encoding, ParseContext context)
    {
        var state = adapter.CreateInitialState(context);
        var entries = new List<ParsedEntry>();
        var issues = new List<LineIssue>();
        var counts = new int[Enum.GetValues<LineOutcome>().Length];

        foreach (var line in lines)
        {
            state = state.AdvanceLine();
            var step = Step(adapter, state, line);
            if (step.State.LineNumber != state.LineNumber)
            {
                throw new InvalidOperationException("Adapter violated the contract: line number changed.");
            }

            state = step.State;
            var result = step.Result;
            counts[(int)result.Outcome]++;
            entries.AddRange(result.Completed);
            if (result.Outcome is LineOutcome.Malformed or LineOutcome.Unsupported)
            {
                issues.Add(new LineIssue(result.LineNumber, result.Outcome, result.Issue));
            }
        }

        var (_, remaining) = state.FlushPending();
        entries.AddRange(remaining);

        var (participants, messages) = ParticipantResolver.Resolve(entries);
        var statistics = new ParseStatistics(
            lines.Count,
            counts[(int)LineOutcome.MessageStarted],
            counts[(int)LineOutcome.SystemMessage],
            counts[(int)LineOutcome.Continuation],
            counts[(int)LineOutcome.Metadata],
            counts[(int)LineOutcome.Ignored],
            counts[(int)LineOutcome.Malformed],
            counts[(int)LineOutcome.Unsupported]);

        return new ParsedChat(adapter.FormatId, encoding, participants, messages, issues, statistics);
    }

    private static LineStep Step(IChatFormatAdapter adapter, ParserState state, string line)
    {
        if (line.Length > state.Context.MaxLineLength)
        {
            return Reject(state, ParseIssue.LineTooLong);
        }

        if (line.Contains('\0', StringComparison.Ordinal))
        {
            return Reject(state, ParseIssue.NullCharacter);
        }

        try
        {
            return adapter.ParseLine(state, line);
        }
        catch (RegexMatchTimeoutException)
        {
            return Reject(state, ParseIssue.PatternTimeout);
        }
    }

    private static LineStep Reject(ParserState state, ParseIssue issue)
    {
        var (flushed, completed) = state.EnterBody().FlushPending();
        return new LineStep(flushed, LineParseResult.Malformed(state.LineNumber, issue, completed));
    }

    private static ChatParseOutcome Fail(ParseFailure failure) => new(failure, null);
}

/// <summary>
/// Maps speaker names to dataset-local participant ids in order of first appearance.
/// Exports carry no user ids, so equal names are one participant until the user splits them;
/// nobody is marked as Owner automatically.
/// </summary>
public static class ParticipantResolver
{
    public static (IReadOnlyList<Participant> Participants, IReadOnlyList<ChatMessage> Messages) Resolve(IReadOnlyList<ParsedEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var ids = new Dictionary<string, ParticipantId>(StringComparer.Ordinal);
        var participants = new List<Participant>();
        var messages = new List<ChatMessage>(entries.Count);

        foreach (var entry in entries)
        {
            ParticipantId? speaker = null;
            if (entry.SpeakerName is { } name)
            {
                if (!ids.TryGetValue(name, out var id))
                {
                    id = new ParticipantId(string.Create(CultureInfo.InvariantCulture, $"p_{ids.Count + 1:D2}"));
                    ids.Add(name, id);
                    participants.Add(new Participant(id, name, ParticipantConsent.ContextOnly));
                }

                speaker = id;
            }

            messages.Add(new ChatMessage(
                new MessageId(string.Create(CultureInfo.InvariantCulture, $"m{entry.StartLine:D7}")),
                entry.Timestamp,
                speaker,
                entry.Kind,
                entry.Text,
                entry.StartLine)
            {
                SystemEvent = entry.Kind == MessageKind.System ? SystemEventClassifier.Classify(entry.Text) : SystemEventType.None,
            });
        }

        return (participants, messages);
    }
}

/// <summary>Rule table for system lines of the synthetic formats (docs/SYNTHETIC_FORMATS.md).</summary>
public static class SystemEventClassifier
{
    private static readonly ImmutableArray<(string Suffix, SystemEventType Type)> Rules =
    [
        (" 님이 들어왔습니다.", SystemEventType.Join),
        (" 님이 나갔습니다.", SystemEventType.Leave),
    ];

    public static SystemEventType Classify(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach (var (suffix, type) in Rules)
        {
            if (text.EndsWith(suffix, StringComparison.Ordinal))
            {
                return type;
            }
        }

        return SystemEventType.Other;
    }

    /// <summary>Extracts the participant name a join/leave line refers to (text before the rule suffix).</summary>
    public static bool TryGetSubject(string text, out string subject)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach (var (suffix, _) in Rules)
        {
            if (text.EndsWith(suffix, StringComparison.Ordinal) && text.Length > suffix.Length)
            {
                subject = text[..^suffix.Length];
                return true;
            }
        }

        subject = string.Empty;
        return false;
    }
}
