using System.Text.Json;
using System.Text.Json.Serialization;

namespace TalkPro.Llm.Ollama;

// Ollama REST API shapes (https://github.com/ollama/ollama/blob/main/docs/api.md). Responses ignore unknown fields.
// ToString never prints members: messages carry prompt and model text.
internal sealed record ChatMessageWire(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content)
{
    public override string ToString() => "ChatMessageWire";
}

internal sealed record GenerationOptionsWire(
    [property: JsonPropertyName("temperature")] double Temperature,
    [property: JsonPropertyName("num_predict")] int NumPredict,
    [property: JsonPropertyName("seed")] int? Seed)
{
    public override string ToString() => "GenerationOptionsWire";
}

internal sealed record ChatRequestWire(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessageWire> Messages,
    [property: JsonPropertyName("stream")] bool Stream,
    [property: JsonPropertyName("format")] JsonElement? Format,
    [property: JsonPropertyName("options")] GenerationOptionsWire Options)
{
    /// <summary>Sent only for reasoning models (false = answer directly); omitted otherwise.</summary>
    [JsonPropertyName("think")]
    public bool? Think { get; init; }

    public override string ToString() => "ChatRequestWire";
}

internal sealed record ChatResponseWire(
    [property: JsonPropertyName("message")] ChatMessageWire? Message,
    [property: JsonPropertyName("done")] bool Done)
{
    public override string ToString() => "ChatResponseWire";
}

internal sealed record EmbedRequestWire(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("input")] IReadOnlyList<string> Input)
{
    public override string ToString() => "EmbedRequestWire";
}

internal sealed record EmbedResponseWire(
    [property: JsonPropertyName("embeddings")] IReadOnlyList<float[]>? Embeddings)
{
    public override string ToString() => "EmbedResponseWire";
}

internal sealed record TagWire(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("digest")] string? Digest)
{
    public override string ToString() => "TagWire";
}

internal sealed record TagsResponseWire(
    [property: JsonPropertyName("models")] IReadOnlyList<TagWire>? Models)
{
    public override string ToString() => "TagsResponseWire";
}

internal sealed record VersionResponseWire(
    [property: JsonPropertyName("version")] string? Version)
{
    public override string ToString() => "VersionResponseWire";
}

internal static class Wire
{
    public static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        MaxDepth = 32,
    };
}
