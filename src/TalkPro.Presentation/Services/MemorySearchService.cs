using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TalkPro.Core.Llm;
using TalkPro.Core.Model;
using TalkPro.Infrastructure.Vault;
using TalkPro.Memory;
using TalkPro.Privacy;
using TalkPro.Presentation.Text;
using TalkPro.Security.Keys;

namespace TalkPro.Presentation.Services;

public enum MemoryError
{
    None,
    Unreadable,
    NotPrivacyReviewed,
    NoOwner,
    EmptyQuery,
    QueryTooLong,

    /// <summary>Building the index needs the local embedding model (see <see cref="MemoryIndexStatus.LlmError"/>).</summary>
    Model,
}

public enum MemoryIndexState
{
    /// <summary>No index yet: search works with keywords only.</summary>
    Missing,

    /// <summary>The data, privacy decisions or chunking rules changed since the index was built.</summary>
    Stale,

    Ready,
}

public enum SearchMode
{
    /// <summary>Embeddings + keywords + recency + participant match.</summary>
    Hybrid,

    /// <summary>No usable index or embedding model: FTS5 keyword match only.</summary>
    KeywordOnly,
}

public sealed record MemoryIndexStatus(MemoryError Error, MemoryIndexState State, int Items, string? Model, LlmError LlmError = LlmError.None)
{
    public override string ToString() => $"MemoryIndexStatus {{ Error = {Error}, State = {State}, Items = {Items} }}";
}

/// <summary>One stored message on an evidence card. Speaker name and text are personal data shown locally only.</summary>
public sealed record EvidenceLine(MessageId Id, string Speaker, string Time, string Text, bool IsOwner)
{
    public override string ToString() => $"EvidenceLine {{ Id = {Id}, Owner = {IsOwner} }}";
}

/// <summary>A QUOTE card: the stored record, rendered from the vault — never generated text.</summary>
public sealed record EvidenceCard(MemoryItemId Item, string DateText, double Score, IReadOnlyList<EvidenceLine> Lines)
{
    public static GroundingLabel Label => GroundingLabel.Quote;

    public override string ToString() => $"EvidenceCard {{ Item = {Item}, Lines = {Lines.Count}, Score = {Score:F3} }}";
}

public sealed record MemorySearchResult(MemoryError Error, SearchMode Mode, IReadOnlyList<EvidenceCard> Cards)
{
    /// <summary>"기록에서 찾지 못했습니다." — the search ran and nothing passed the evidence threshold.</summary>
    public bool NotFound => Error == MemoryError.None && Cards.Count == 0;

    public override string ToString() => $"MemorySearchResult {{ Error = {Error}, Mode = {Mode}, Cards = {Cards.Count} }}";
}

public interface IMemorySearchService
{
    Task<MemoryIndexStatus> GetStatusAsync(DatasetId id, CancellationToken cancellationToken);

    /// <summary>Embeds every memory item with the local embedding model and stores the encrypted index. Progress is 0..1.</summary>
    Task<MemoryIndexStatus> BuildIndexAsync(DatasetId id, IProgress<double>? progress, CancellationToken cancellationToken);

    Task<MemorySearchResult> SearchAsync(DatasetId id, string query, CancellationToken cancellationToken);
}

/// <summary>
/// Memory search (Phase 7, DD-025). Everything runs on this PC:
/// <list type="bullet">
/// <item>Items: sessions split into turn windows that contain an owner message (counterparts only as context).</item>
/// <item>Embeddings: the LlmPrompt view (names as [인물N], excluded messages absent) through the local embedding model,
/// stored encrypted with the dataset key; the query is redacted with the same pseudonym map.</item>
/// <item>Keywords: FTS5 over the LocalSearch view (original names, high-risk identifiers masked, excluded absent).</item>
/// <item>Evidence cards render the LocalSearch view straight from the vault (QUOTE); nothing is generated.</item>
/// </list>
/// Queries and results are never stored or logged.
/// </summary>
public sealed class MemorySearchService(DatasetRepository repository, ISessionizer sessionizer, IPrivacyScanner scanner, IEmbeddingClient embedder) : IMemorySearchService
{
    public const int MaxQueryLength = 200;
    public const int BatchSize = 32;
    private const int DenseCandidates = 50;

    public Task<MemoryIndexStatus> GetStatusAsync(DatasetId id, CancellationToken cancellationToken) =>
        Task.Run(() => WithData(id, data => Status(data, data.Index)), cancellationToken);

    public async Task<MemoryIndexStatus> BuildIndexAsync(DatasetId id, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var loaded = await Task.Run(() => Load(id), cancellationToken).ConfigureAwait(false);
        if (loaded.Error != MemoryError.None)
        {
            return new MemoryIndexStatus(loaded.Error, MemoryIndexState.Missing, 0, null);
        }

        var data = loaded.Data!;
        VectorIndex? index = null;
        for (var start = 0; start < data.Items.Count; start += BatchSize)
        {
            var batch = data.Items.Skip(start).Take(BatchSize).ToList();
            var result = await embedder.EmbedAsync([.. batch.Select(data.EmbeddingText)], cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                return new MemoryIndexStatus(MemoryError.Model, data.Index is null ? MemoryIndexState.Missing : MemoryIndexState.Stale, 0, null, result.Error);
            }

            index ??= new VectorIndex(result.Model!.ModelId, result.Model.Digest, data.Fingerprint, result.Vectors![0].Length);
            for (var i = 0; i < batch.Count; i++)
            {
                index.Add(batch[i], result.Vectors![i]);
            }

            progress?.Report((double)Math.Min(data.Items.Count, start + BatchSize) / data.Items.Count);
        }

        index ??= new VectorIndex("-", string.Empty, data.Fingerprint, 1);
        var bytes = index.Serialize();
        var saved = await Task.Run(() => WithSession(id, session =>
        {
            session.SaveMemoryIndex(bytes);
            return true;
        }), cancellationToken).ConfigureAwait(false);
        return saved
            ? new MemoryIndexStatus(MemoryError.None, MemoryIndexState.Ready, index.Items.Count, index.ModelId)
            : new MemoryIndexStatus(MemoryError.Unreadable, MemoryIndexState.Missing, 0, null);
    }

    public async Task<MemorySearchResult> SearchAsync(DatasetId id, string query, CancellationToken cancellationToken)
    {
        var text = (query ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return new MemorySearchResult(MemoryError.EmptyQuery, SearchMode.KeywordOnly, []);
        }

        if (text.Length > MaxQueryLength)
        {
            return new MemorySearchResult(MemoryError.QueryTooLong, SearchMode.KeywordOnly, []);
        }

        var loaded = await Task.Run(() => Load(id, KoreanQuery.Terms(text)), cancellationToken).ConfigureAwait(false);
        if (loaded.Error != MemoryError.None)
        {
            return new MemorySearchResult(loaded.Error, SearchMode.KeywordOnly, []);
        }

        var data = loaded.Data!;
        var dense = new Dictionary<MemoryItemId, double>();
        var mode = SearchMode.KeywordOnly;
        if (Status(data, data.Index).State == MemoryIndexState.Ready && data.Index!.Items.Count > 0)
        {
            var embedded = await embedder.EmbedAsync([PromptInput.Redact(text, data.Participants, scanner, data.Pseudonyms)], cancellationToken).ConfigureAwait(false);
            if (embedded.Succeeded && embedded.Model!.Digest == data.Index.ModelDigest && embedded.Vectors![0].Length == data.Index.Dimension)
            {
                foreach (var (item, score) in data.Index.Search(embedded.Vectors[0], DenseCandidates))
                {
                    dense[item.Id] = score;
                }

                mode = SearchMode.Hybrid;
            }
        }

        var mentioned = data.Participants
            .Where(p => new[] { p.DisplayName }.Concat(p.Aliases).Any(n => n.Length >= 2 && text.Contains(n, StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Id)
            .ToHashSet();
        var ranked = HybridRetriever.Rank(
            data.Items,
            dense,
            data.Sparse,
            data.Latest,
            item => item.SourceMessageIds.Any(m => data.SearchView.TryGetValue(m, out var msg) && msg.SpeakerId is { } s && mentioned.Contains(s)),
            mode == SearchMode.Hybrid ? null : new RetrievalWeights { Dense = 0 });

        // Last filter (report §6.3, leak prevention): never show a message that is excluded or has no search view.
        var cards = ranked
            .Where(r => r.Item.SourceMessageIds.All(m => !data.Excluded.Contains(m) && data.SearchView.ContainsKey(m)))
            .Select(r => Card(r, data))
            .ToList();
        return new MemorySearchResult(MemoryError.None, mode, cards);
    }

    private static EvidenceCard Card(ScoredItem scored, Data data)
    {
        var lines = scored.Item.SourceMessageIds.Select(id =>
        {
            var message = data.SearchView[id];
            var speaker = message.SpeakerId is { } s && data.Names.TryGetValue(s, out var name) ? name : "-";
            return new EvidenceLine(id, speaker, message.Timestamp.ToString("HH:mm", UiText.Korean), message.Text, message.SpeakerId == data.Owner);
        }).ToList();
        return new EvidenceCard(scored.Item.Id, scored.Item.Start.ToString("yyyy-MM-dd (ddd)", UiText.Korean), scored.Score, lines);
    }

    private static MemoryIndexStatus Status(Data data, VectorIndex? index) => index is null
        ? new MemoryIndexStatus(MemoryError.None, MemoryIndexState.Missing, data.Items.Count, null)
        : new MemoryIndexStatus(MemoryError.None, index.Fingerprint == data.Fingerprint ? MemoryIndexState.Ready : MemoryIndexState.Stale, index.Items.Count, index.ModelId);

    /// <summary>Hash of everything the embeddings depend on: chunking rules, item composition and the LlmPrompt text.</summary>
    private static string Fingerprint(IReadOnlyList<MemoryItem> items, Dictionary<MessageId, RedactedText> redacted)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"chunker:{MemoryChunker.Version}\n")));
        foreach (var item in items)
        {
            foreach (var id in item.SourceMessageIds)
            {
                hash.AppendData(Encoding.UTF8.GetBytes(id.Value + "\0" + redacted[id].Value + "\n"));
            }

            hash.AppendData("\u001e"u8);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private MemoryIndexStatus WithData(DatasetId id, Func<Data, MemoryIndexStatus> read)
    {
        var loaded = Load(id);
        return loaded.Error == MemoryError.None ? read(loaded.Data!) : new MemoryIndexStatus(loaded.Error, MemoryIndexState.Missing, 0, null);
    }

    private Loaded Load(DatasetId id, IReadOnlyList<string>? terms = null)
    {
        Loaded? loaded = null;
        var opened = WithSession(id, session =>
        {
            if (!session.PrivacyReviewed)
            {
                loaded = new Loaded(MemoryError.NotPrivacyReviewed, null);
                return true;
            }

            if (session.OwnerId is not { } owner)
            {
                loaded = new Loaded(MemoryError.NoOwner, null);
                return true;
            }

            var messages = session.LoadMessages();
            var findings = session.LoadFindings();
            var excluded = session.LoadExcludedMessageIds();
            var participants = session.LoadParticipants();
            var pseudonyms = new PseudonymMap();
            var llm = PurposeRedactor.RenderForLlm(messages, findings, excluded, pseudonyms);
            var view = llm.Select(v => v.Source).ToList();
            var redacted = llm.ToDictionary(v => v.Source.Id, v => v.Text);
            var items = MemoryChunker.Build(owner, view, sessionizer.Split(view));
            var search = PurposeRedactor.RenderAll(messages, findings, excluded, RedactionPurpose.LocalSearch).ToDictionary(m => m.Id);
            VectorIndex? index = null;
            try
            {
                index = session.LoadMemoryIndex() is { } bytes ? VectorIndex.Deserialize(bytes) : null;
            }
            catch (Exception ex) when (ex is CryptographicException or InvalidDataException)
            {
                index = null; // Damaged index: treated as missing and rebuilt on request.
            }

            loaded = new Loaded(MemoryError.None, new Data(
                owner,
                participants,
                participants.ToDictionary(p => p.Id, p => p.DisplayName),
                pseudonyms,
                items,
                redacted,
                search,
                excluded,
                terms is { Count: > 0 } ? session.SearchRanked(terms) : new Dictionary<MessageId, double>(),
                messages.Count == 0 ? DateTimeOffset.UnixEpoch : messages.Max(m => m.Timestamp),
                Fingerprint(items, redacted),
                index));
            return true;
        });
        return opened && loaded is not null ? loaded : new Loaded(MemoryError.Unreadable, null);
    }

    private bool WithSession(DatasetId id, Func<DatasetSession, bool> action)
    {
        try
        {
            using var session = repository.Open(id);
            return action(session);
        }
        catch (Exception ex) when (ex is DatasetKeyNotFoundException or CryptographicException or InvalidDataException or JsonException or IOException)
        {
            return false;
        }
    }

    private sealed record Loaded(MemoryError Error, Data? Data)
    {
        public override string ToString() => $"Loaded {{ Error = {Error} }}";
    }

    /// <summary>One dataset prepared for indexing or search. Memory only; disposed with the call.</summary>
    private sealed record Data(
        ParticipantId Owner,
        IReadOnlyList<Participant> Participants,
        IReadOnlyDictionary<ParticipantId, string> Names,
        PseudonymMap Pseudonyms,
        IReadOnlyList<MemoryItem> Items,
        IReadOnlyDictionary<MessageId, RedactedText> Redacted,
        IReadOnlyDictionary<MessageId, ChatMessage> SearchView,
        IReadOnlySet<MessageId> Excluded,
        IReadOnlyDictionary<MessageId, double> Sparse,
        DateTimeOffset Latest,
        string Fingerprint,
        VectorIndex? Index)
    {
        /// <summary>One item as embedding input: "나: …" / "상대: …" lines of the LlmPrompt view (no names).</summary>
        public RedactedText EmbeddingText(MemoryItem item) =>
            PromptInput.Join([.. item.SourceMessageIds.Select(id =>
                new ContextLine(SearchView.TryGetValue(id, out var m) && m.SpeakerId == Owner ? "나" : "상대", Redacted[id]))]);

        public override string ToString() => $"Data {{ Items = {Items.Count} }}";
    }
}
