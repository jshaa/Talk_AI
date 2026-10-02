using System.Collections.Immutable;
using System.Globalization;
using TalkPro.Core.Model;
using TalkPro.Core.Parsing;

namespace TalkPro.Ingestion.Import;

public enum ImportStatus
{
    Success,

    /// <summary>The decoded input has no lines (0 bytes or BOM only).</summary>
    EmptyInput,

    InputTooLarge,

    /// <summary>Neither UTF-8 nor CP949 without loss (DD-011).</summary>
    UnsupportedEncoding,

    /// <summary>No adapter recognised the content, or two adapters were equally confident.</summary>
    UnknownFormat,
}

/// <summary>Outcome of an import. <see cref="Chat"/> is set only on <see cref="ImportStatus.Success"/>.</summary>
public sealed record ChatImportResult
{
    private ChatImportResult(ImportStatus status, ParsedChat? chat)
    {
        Status = status;
        Chat = chat;
    }

    public ImportStatus Status { get; }

    public ParsedChat? Chat { get; }

    public static ChatImportResult Failed(ImportStatus status) =>
        status == ImportStatus.Success ? throw new ArgumentOutOfRangeException(nameof(status)) : new(status, null);

    public static ChatImportResult Succeeded(ParsedChat chat)
    {
        ArgumentNullException.ThrowIfNull(chat);
        return new(ImportStatus.Success, chat);
    }

    public override string ToString() => $"ChatImportResult {{ Status = {Status}, Chat = {Chat} }}";
}

/// <summary>
/// Ingestion driver (DD-009): decodes, splits lines, selects an adapter, applies the line guards
/// (<see cref="ParseContext.MaxLineLength"/>, U+0000), advances <see cref="ParserState.LineNumber"/>,
/// enforces the adapter contract and maps completed entries to the Core model.
/// Performs no I/O and no logging; results carry only content-free statistics for diagnostics.
/// </summary>
public sealed class ChatExportParser
{
    public const int MaxInputBytes = 256 * 1024 * 1024;

    private readonly ImmutableArray<IChatFormatAdapter> _adapters;

    public ChatExportParser(IEnumerable<IChatFormatAdapter> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        _adapters = [.. adapters];
        if (_adapters.IsEmpty || _adapters.Any(a => a is null))
        {
            throw new ArgumentException("At least one adapter is required and none may be null.", nameof(adapters));
        }

        if (_adapters.Select(a => a.FormatId).Distinct().Count() != _adapters.Length)
        {
            throw new ArgumentException("Format ids must be unique.", nameof(adapters));
        }
    }

    public ImmutableArray<IChatFormatAdapter> Adapters => _adapters;

    /// <summary>Imports with content-based format detection.</summary>
    public ChatImportResult Parse(ReadOnlySpan<byte> content, ParseContext context) =>
        Import(content, context, lines => FormatDetector.Select(_adapters, lines, context));

    /// <summary>Imports with an explicitly chosen adapter (no detection).</summary>
    public static ChatImportResult Parse(ReadOnlySpan<byte> content, ParseContext context, IChatFormatAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        return Import(content, context, _ => adapter);
    }

    /// <summary>Runs <paramref name="adapter"/> over already decoded lines.</summary>
    public static ParsedChat ParseLines(IReadOnlyList<string> lines, IChatFormatAdapter adapter, ParseContext context, SourceEncoding encoding)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(context);

        var state = adapter.CreateInitialState(context);
        if (state is null || state.LineNumber != 0 || !ReferenceEquals(state.Context, context))
        {
            throw new InvalidOperationException("Format adapter returned an invalid initial parser state.");
        }

        var entries = new List<ParsedEntry>();
        var issues = new List<LineIssue>();
        var counts = new int[Enum.GetValues<LineOutcome>().Length];

        foreach (var line in lines)
        {
            ArgumentNullException.ThrowIfNull(line, nameof(lines));
            state = state.AdvanceLine();
            var step = Guard(state, line) ?? adapter.ParseLine(state, line);
            if (step.State is null || step.Result is null
                || step.State.LineNumber != state.LineNumber
                || step.Result.LineNumber != state.LineNumber
                || !ReferenceEquals(step.State.Context, context))
            {
                throw new InvalidOperationException("Format adapter violated the line contract (line number, context or null result).");
            }

            var result = step.Result;
            counts[(int)result.Outcome]++;
            if (result.Outcome is LineOutcome.Malformed or LineOutcome.Unsupported)
            {
                issues.Add(new LineIssue(result.LineNumber, result.Outcome, result.Issue));
            }

            entries.AddRange(result.Completed);
            state = step.State;
        }

        entries.AddRange(state.FlushPending().Completed);
        return BuildChat(adapter.FormatId, encoding, lines.Count, entries, issues, counts);
    }

    private static ChatImportResult Import(ReadOnlySpan<byte> content, ParseContext context, Func<IReadOnlyList<string>, IChatFormatAdapter?> selectAdapter)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (content.Length > MaxInputBytes)
        {
            return ChatImportResult.Failed(ImportStatus.InputTooLarge);
        }

        if (!EncodingDetector.TryDecode(content, out var encoding, out var text))
        {
            return ChatImportResult.Failed(ImportStatus.UnsupportedEncoding);
        }

        var lines = LineSplitter.Split(text);
        if (lines.Count == 0)
        {
            return ChatImportResult.Failed(ImportStatus.EmptyInput);
        }

        var adapter = selectAdapter(lines);
        return adapter is null
            ? ChatImportResult.Failed(ImportStatus.UnknownFormat)
            : ChatImportResult.Succeeded(ParseLines(lines, adapter, context, encoding));
    }

    /// <summary>Driver guards run before the adapter sees the line; a rejected line completes the open message.</summary>
    private static LineStep? Guard(ParserState state, string line)
    {
        ParseIssue issue;
        if (line.Length > state.Context.MaxLineLength)
        {
            issue = ParseIssue.LineTooLong;
        }
        else if (line.Contains('\0', StringComparison.Ordinal))
        {
            issue = ParseIssue.NullCharacter;
        }
        else
        {
            return null;
        }

        var (flushed, completed) = state.FlushPending();
        return new LineStep(flushed, LineParseResult.Malformed(state.LineNumber, issue, completed));
    }

    private static ParsedChat BuildChat(
        ChatFormatId formatId,
        SourceEncoding encoding,
        int totalLines,
        List<ParsedEntry> entries,
        List<LineIssue> issues,
        int[] counts)
    {
        var participantIds = new Dictionary<string, ParticipantId>(StringComparer.Ordinal);
        var participants = new List<Participant>();
        var messages = new List<ChatMessage>(entries.Count);
        foreach (var entry in entries)
        {
            ParticipantId? speakerId = null;
            if (entry.SpeakerName is { } name)
            {
                if (!participantIds.TryGetValue(name, out var id))
                {
                    id = new ParticipantId("p_" + (participants.Count + 1).ToString("D2", CultureInfo.InvariantCulture));
                    participantIds.Add(name, id);

                    // Owner is never inferred (DD-006): everyone starts as context-only.
                    participants.Add(new Participant(id, name, ParticipantConsent.ContextOnly));
                }

                speakerId = id;
            }

            var messageId = new MessageId("m" + entry.StartLine.ToString("D7", CultureInfo.InvariantCulture));
            messages.Add(new ChatMessage(messageId, entry.Timestamp, speakerId, entry.Kind, entry.Text, entry.StartLine)
            {
                SystemEvent = entry.SystemEvent,
            });
        }

        var statistics = new ParseStatistics(
            totalLines,
            MessagesStarted: counts[(int)LineOutcome.MessageStarted],
            SystemMessages: counts[(int)LineOutcome.SystemMessage],
            Continuations: counts[(int)LineOutcome.Continuation],
            Metadata: counts[(int)LineOutcome.Metadata],
            Ignored: counts[(int)LineOutcome.Ignored],
            Malformed: counts[(int)LineOutcome.Malformed],
            Unsupported: counts[(int)LineOutcome.Unsupported]);

        return new ParsedChat(formatId, encoding, participants, messages, issues, statistics);
    }
}
