using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace TalkPro.Core.Llm;

/// <summary>
/// Strict JSON for structured model output: the schema is derived from the C# type and sent to the runtime,
/// and the reply is parsed strictly (unknown members, missing required members and nulls in non-nullable
/// members are rejected). Enums travel as exact snake_case names ("ask_back"): the schema lists them, and
/// parsing rejects numbers and any other spelling ("Accept", "ACCEPT"). Parsing never throws; a failure is
/// reported as false.
/// </summary>
public static class StructuredOutput
{
    public const int MaxDepth = 16;

    private static readonly JsonSerializerOptions Base = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        NumberHandling = JsonNumberHandling.Strict,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        PropertyNameCaseInsensitive = false,
        MaxDepth = MaxDepth,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
    };

    // The built-in converter lets the schema exporter list enum values ("enum": [...]).
    private static readonly JsonSerializerOptions Schema = new(Base)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) },
    };

    // The built-in converter reads names case-insensitively, so parsing uses an exact-name converter instead.
    private static readonly JsonSerializerOptions Parse = new(Base)
    {
        Converters = { new ExactSnakeCaseEnumConverter() },
    };

    public static string SchemaFor<T>() =>
        JsonSchemaExporter.GetJsonSchemaAsNode(Schema, typeof(T), new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true })
            .ToJsonString();

    public static bool TryParse<T>(string? json, [NotNullWhen(true)] out T? value)
        where T : class
    {
        value = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            value = JsonSerializer.Deserialize<T>(json, Parse);
            return value is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

/// <summary>Enum values as exact snake_case names only: no numbers, no other casing.</summary>
public sealed class ExactSnakeCaseEnumConverter : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert is { IsEnum: true };

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(Exact<>).MakeGenericType(typeToConvert))!;

    public override string ToString() => nameof(ExactSnakeCaseEnumConverter);

    public sealed class Exact<TEnum> : JsonConverter<TEnum>
        where TEnum : struct, Enum
    {
        private static readonly Dictionary<string, TEnum> ByName =
            Enum.GetValues<TEnum>().ToDictionary(v => JsonNamingPolicy.SnakeCaseLower.ConvertName(v.ToString()), StringComparer.Ordinal);

        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.String && ByName.TryGetValue(reader.GetString()!, out var value)
                ? value
                : throw new JsonException("Unknown enum value.");

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.WriteStringValue(JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString()));
        }

        public override string ToString() => $"Exact<{typeof(TEnum).Name}>";
    }
}
