using TalkPro.Core.Diagnostics;

namespace TalkPro.Core.Llm;

/// <summary>
/// One line of conversation context: an app-authored label ("상대", "나") and redacted text.
/// Labels are short, single-line and cannot contain the context delimiters.
/// </summary>
public sealed record ContextLine : ILogSafe
{
    public const int MaxLabelLength = 20;

    public ContextLine(string label, RedactedText text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(text);
        if (label.Length > MaxLabelLength || label.Any(c => char.IsControl(c) || c is '[' or ']'))
        {
            throw new ArgumentException("A context label must be a short single-line app string.", nameof(label));
        }

        Label = label;
        Text = text;
    }

    public string Label { get; }

    public RedactedText Text { get; }

    public override string ToString() => $"ContextLine {{ Label = {Label}, {Text} }}";
}

/// <summary>
/// One generation request. <see cref="Instruction"/> and <see cref="Task"/> are app-authored templates and
/// must never embed conversation text; conversation data goes only through <see cref="Context"/>.
/// </summary>
public sealed record LlmRequest(string Instruction, IReadOnlyList<ContextLine> Context, string Task) : ILogSafe
{
    /// <summary>JSON schema for structured output (see <see cref="StructuredOutput.SchemaFor{T}"/>); null for free text.</summary>
    public string? JsonSchema { get; init; }

    public double Temperature { get; init; } = 0.7;

    public int MaxOutputTokens { get; init; } = 512;

    /// <summary>Fixed seed for reproducible tests; null in normal use.</summary>
    public int? Seed { get; init; }

    public override string ToString() =>
        $"LlmRequest {{ Context = {Context.Count}, Structured = {JsonSchema is not null}, MaxOutputTokens = {MaxOutputTokens} }}";
}

public enum LlmError
{
    None,

    /// <summary>The local runtime (e.g. Ollama) is not running or not installed.</summary>
    RuntimeNotRunning,

    /// <summary>No model for this role is installed in the runtime.</summary>
    ModelNotInstalled,

    /// <summary>A registry model is installed but not usable in this build (approval policy or digest mismatch).</summary>
    ModelNotAllowed,

    Timeout,

    ResponseTooLarge,

    InvalidResponse,

    Cancelled,
}

/// <summary>Which model produced an output (shown with the "AI 생성" label).</summary>
public sealed record ModelProvenance(string ModelId, string Digest) : ILogSafe
{
    public override string ToString() => $"ModelProvenance {{ Model = {ModelId} }}";
}

/// <summary>Generated text. Always AI-generated content (AI Basic Act labelling, master prompt §17).</summary>
public sealed record LlmResult(LlmError Error, string? Text, ModelProvenance? Model, TimeSpan Elapsed) : ILogSafe
{
    public bool Succeeded => Error == LlmError.None && Text is not null;

    public static bool IsAiGenerated => true;

    public static LlmResult Failed(LlmError error, TimeSpan elapsed = default) => new(error, null, null, elapsed);

    public override string ToString() => $"LlmResult {{ Error = {Error}, Length = {Text?.Length ?? 0}, Model = {Model} }}";
}

public sealed record EmbeddingResult(LlmError Error, IReadOnlyList<float[]>? Vectors, ModelProvenance? Model) : ILogSafe
{
    public bool Succeeded => Error == LlmError.None && Vectors is not null;

    public static EmbeddingResult Failed(LlmError error) => new(error, null, null);

    public override string ToString() => $"EmbeddingResult {{ Error = {Error}, Vectors = {Vectors?.Count ?? 0}, Model = {Model} }}";
}

/// <summary>Text generation on this device only (Ollama on loopback now; an in-process runtime later, DD-014/DD-023).</summary>
public interface ILlmClient
{
    Task<LlmResult> GenerateAsync(LlmRequest request, CancellationToken cancellationToken);
}

/// <summary>Local embeddings for memory search (Phase 7).</summary>
public interface IEmbeddingClient
{
    Task<EmbeddingResult> EmbedAsync(IReadOnlyList<RedactedText> texts, CancellationToken cancellationToken);
}
