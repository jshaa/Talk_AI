using System.Security.Cryptography;
using System.Text.Json;
using TalkPro.Core.Llm;
using TalkPro.Core.Model;
using TalkPro.Infrastructure.Vault;
using TalkPro.Persona.Analysis;
using TalkPro.Persona.Style;
using TalkPro.Privacy;
using TalkPro.Security.Keys;

namespace TalkPro.Presentation.Services;

public enum DraftError
{
    None,
    Unreadable,
    NotPrivacyReviewed,
    NoOwner,
    EmptyInput,
    InputTooLong,

    /// <summary>The local model failed (see <see cref="DraftSet.LlmError"/>).</summary>
    Model,

    /// <summary>
    /// After one retry the model still gave no usable draft: wrong JSON shape, a missing or wrong
    /// <c>direction</c>, or every draft failed the clean-up or safety check.
    /// </summary>
    InvalidOutput,
}

/// <summary>Someone the owner may be replying to (for the relationship-scoped style). Display name is personal data.</summary>
public sealed record DraftTarget(ParticipantId Id, string DisplayName)
{
    public override string ToString() => $"DraftTarget {{ Id = {Id} }}";
}

/// <summary>One draft for the requested direction at <see cref="Slot"/> (index into the requested directions).</summary>
public sealed record DraftItem(int Slot, ReplyDirection Direction, string Text, int StyleMatch)
{
    public override string ToString() => $"DraftItem {{ Slot = {Slot}, Direction = {Direction}, Length = {Text.Length}, StyleMatch = {StyleMatch} }}";
}

/// <summary>Reply drafts. Always AI-generated; never sent automatically (there is no send API at all).</summary>
public sealed record DraftSet(
    DraftError Error,
    LlmError LlmError,
    IReadOnlyList<DraftItem> Drafts,
    IReadOnlyList<string> StyleSummary,
    int ExampleCount,
    ModelProvenance? Model)
{
    /// <summary>Requested slots that stayed empty after the retry (their direction never came back usable).</summary>
    public IReadOnlyList<int> MissingSlots { get; init; } = [];

    public static DraftSet Failed(DraftError error, LlmError llmError = LlmError.None) => new(error, llmError, [], [], 0, null);

    public override string ToString() =>
        $"DraftSet {{ Error = {Error}, LlmError = {LlmError}, Drafts = {Drafts.Count}, Missing = {MissingSlots.Count}, Examples = {ExampleCount} }}";
}

public interface IReplyDraftService
{
    /// <summary>Participants the owner may be replying to (owner and excluded people are not listed). Null if unreadable.</summary>
    Task<IReadOnlyList<DraftTarget>?> LoadTargetsAsync(DatasetId id, CancellationToken cancellationToken);

    /// <summary>
    /// One draft per entry of <paramref name="directions"/> (1–3; duplicates allowed). A full generation passes
    /// <see cref="ReplyDirections.Default"/>; regenerating one card passes just that card's direction.
    /// </summary>
    Task<DraftSet> DraftAsync(DatasetId id, ParticipantId? counterpart, string incoming, IReadOnlyList<ReplyDirection> directions, CancellationToken cancellationToken);
}

/// <summary>
/// "내 말투로 답장 초안" (Phase 6/6.6, DD-024). Everything sent to the model is the LlmPrompt view plus the typed
/// message after redaction, with one pseudonym map so tokens are consistent; real names are restored only
/// for local display. Each requested direction is a slot filled only by a draft with exactly that
/// <c>direction</c>; empty slots are asked for once more. The typed message and drafts are never stored or logged.
/// </summary>
public sealed class ReplyDraftService(DatasetRepository repository, ISessionizer sessionizer, IPrivacyScanner scanner, ILlmClient llm) : IReplyDraftService
{
    public const int MaxInputLength = 1000;

    /// <summary>Identifier categories a draft may contain only if the typed message already had them.</summary>
    private static readonly PiiCategory[] NoInvention = [PiiCategory.HighRiskIdentifier, PiiCategory.Identifier];

    public Task<IReadOnlyList<DraftTarget>?> LoadTargetsAsync(DatasetId id, CancellationToken cancellationToken) =>
        Task.Run(() => Open<IReadOnlyList<DraftTarget>>(id, session => [.. session.LoadParticipants()
            .Where(p => p.Consent is not (ParticipantConsent.Owner or ParticipantConsent.Excluded))
            .Select(p => new DraftTarget(p.Id, p.DisplayName))]), cancellationToken);

    public async Task<DraftSet> DraftAsync(
        DatasetId id, ParticipantId? counterpart, string incoming, IReadOnlyList<ReplyDirection> directions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(directions);
        if (directions.Count is < 1 or > ReplyPromptBuilder.DraftCount)
        {
            throw new ArgumentOutOfRangeException(nameof(directions), "Between one and three directions are allowed.");
        }

        var text = (incoming ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return DraftSet.Failed(DraftError.EmptyInput);
        }

        if (text.Length > MaxInputLength)
        {
            return DraftSet.Failed(DraftError.InputTooLong);
        }

        var prepared = await Task.Run(() => Prepare(id, counterpart, text), cancellationToken).ConfigureAwait(false);
        if (prepared.Error is { } error)
        {
            return DraftSet.Failed(error);
        }

        var (profile, pseudonyms, examples, input) = prepared.Value!;
        var slots = new DraftItem?[directions.Count];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        ModelProvenance? model = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var pending = Enumerable.Range(0, slots.Length).Where(i => slots[i] is null).ToList();
            if (pending.Count == 0)
            {
                break;
            }

            var wanted = pending.Select(i => directions[i]).ToList();
            var request = ReplyPromptBuilder.Build(profile, examples, input, wanted) with { Temperature = attempt == 0 ? 0.8 : 0.4 };
            var result = await llm.GenerateAsync(request, cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                if (slots.All(d => d is null))
                {
                    return DraftSet.Failed(DraftError.Model, result.Error);
                }

                break;
            }

            model ??= result.Model;
            if (!StructuredOutput.TryParse<ReplyDraftOutput>(result.Text, out var output))
            {
                continue;
            }

            var assigned = output.Assign(wanted);
            for (var k = 0; k < pending.Count; k++)
            {
                if (assigned[k] is { } wire
                    && DraftPostProcessor.CleanOne(wire.Text) is { } cleaned
                    && DirectionShape.IsPlausible(wire.Direction, cleaned)
                    && pseudonyms.Restore(cleaned) is var restored
                    && !InventsIdentifiers(restored, text)
                    && seen.Add(restored))
                {
                    slots[pending[k]] = new DraftItem(pending[k], directions[pending[k]], restored, StyleMatch.Score(profile, restored));
                }
            }
        }

        if (slots.All(d => d is null))
        {
            return DraftSet.Failed(DraftError.InvalidOutput);
        }

        return new DraftSet(DraftError.None, LlmError.None, [.. slots.OfType<DraftItem>()], profile.Summary(), examples.Count, model)
        {
            MissingSlots = [.. Enumerable.Range(0, slots.Length).Where(i => slots[i] is null)],
        };
    }

    private Prepared Prepare(DatasetId id, ParticipantId? counterpart, string incoming) =>
        Open(id, session =>
        {
            if (!session.PrivacyReviewed)
            {
                return new Prepared(DraftError.NotPrivacyReviewed, null);
            }

            if (session.OwnerId is not { } owner)
            {
                return new Prepared(DraftError.NoOwner, null);
            }

            var participants = session.LoadParticipants();
            var pseudonyms = new PseudonymMap();
            var view = PurposeRedactor.RenderForLlm(session.LoadMessages(), session.LoadFindings(), session.LoadExcludedMessageIds(), pseudonyms);
            var messages = view.Select(v => v.Source).ToList();
            var redacted = view.ToDictionary(v => v.Source.Id, v => v.Text);
            var sessions = sessionizer.Split(messages);
            var profile = StyleProfile.From(CommunicationAnalyzer.Analyze(owner, participants, messages, sessions), counterpart);

            var input = PromptInput.Redact(incoming, participants, scanner, pseudonyms);
            var examples = StyleExamples.Select(owner, messages, sessions, counterpart, input.Value)
                .Select(e => (redacted[e.Prompt], redacted[e.Reply]))
                .ToList();
            return new Prepared(null, new PreparedRequest(profile, pseudonyms, examples, input));
        }) ?? new Prepared(DraftError.Unreadable, null);

    /// <summary>Safety check: a draft may not contain phone numbers, account numbers, addresses etc. that the typed message did not have.</summary>
    private bool InventsIdentifiers(string draft, string incoming)
    {
        var message = new ChatMessage(new MessageId("draft"), DateTimeOffset.UnixEpoch, null, MessageKind.Text, draft, 0);
        return scanner.Scan([message], [])
            .Where(f => NoInvention.Contains(f.Category))
            .Any(f => !incoming.Contains(draft.Substring(f.Start, f.Length), StringComparison.Ordinal));
    }

    private T? Open<T>(DatasetId id, Func<DatasetSession, T> read)
        where T : class
    {
        try
        {
            using var session = repository.Open(id);
            return read(session);
        }
        catch (Exception ex) when (ex is DatasetKeyNotFoundException or CryptographicException or InvalidDataException or JsonException or IOException)
        {
            return null;
        }
    }

    private sealed record Prepared(DraftError? Error, PreparedRequest? Value)
    {
        public override string ToString() => $"Prepared {{ Error = {Error} }}";
    }

    /// <summary>Everything needed to build a request for any set of directions. Held in memory for one call only.</summary>
    private sealed record PreparedRequest(
        StyleProfile Profile, PseudonymMap Pseudonyms, IReadOnlyList<(RedactedText Prompt, RedactedText Reply)> Examples, RedactedText Input)
    {
        public override string ToString() => $"PreparedRequest {{ Examples = {Examples.Count} }}";
    }
}
