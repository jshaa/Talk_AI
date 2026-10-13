using TalkPro.Core.Llm;
using TalkPro.Llm.Ollama;
using Microsoft.Extensions.Logging.Abstractions;

namespace TalkPro.Llm.Tests;

public sealed class ModelRegistryTests
{
    private static readonly Uri License = new("https://example.invalid/license");

    [Fact]
    public void ShippedRegistryIsConsistentAndLicensed()
    {
        var registry = ModelRegistry.Default;

        Assert.Contains(registry.Models, m => m.Role == ModelRole.Chat);
        Assert.Contains(registry.Models, m => m.Role == ModelRole.Embedding);
        Assert.All(registry.Models, m =>
        {
            Assert.False(string.IsNullOrWhiteSpace(m.License));
            Assert.Equal(Uri.UriSchemeHttps, m.LicenseUrl.Scheme);
        });
        Assert.Equal(ModelApproval.LicenseReview, registry.Models.Single(m => m.Id == "gemma3:4b").Approval);
    }

    [Fact]
    public void ApprovedModelsMustBePinned() =>
        Assert.Throws<ArgumentException>(() => new ModelRegistry([new ModelSpec("x:1b", "X", ModelRole.Chat, "MIT", License, ModelApproval.Approved)]));

    [Theory]
    [InlineData("ABCDEF")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void PinnedDigestsMustBeLowercaseSha256(string digest) =>
        Assert.Throws<ArgumentException>(() => new ModelRegistry(
        [
            new ModelSpec("x:1b", "X", ModelRole.Chat, "MIT", License, ModelApproval.Candidate) { PinnedDigests = new HashSet<string>(StringComparer.Ordinal) { digest } },
        ]));

    [Fact]
    public void DuplicateIdsAreRejectedAfterNormalisation() =>
        Assert.Throws<ArgumentException>(() => new ModelRegistry(
        [
            new ModelSpec("embed", "E", ModelRole.Embedding, "MIT", License, ModelApproval.Candidate),
            new ModelSpec("Embed:latest", "E2", ModelRole.Embedding, "MIT", License, ModelApproval.Candidate),
        ]));

    [Theory]
    [InlineData("bge-m3", "bge-m3:latest")]
    [InlineData(" Qwen3:4B ", "qwen3:4b")]
    public void IdsAreNormalisedLikeOllama(string id, string expected) => Assert.Equal(expected, ModelRegistry.Normalize(id));

    [Fact]
    public void ReleasePolicyAllowsOnlyApproved()
    {
        Assert.True(ModelPolicy.ReleaseDefault.Allows(ModelApproval.Approved));
        Assert.False(ModelPolicy.ReleaseDefault.Allows(ModelApproval.Candidate));
        Assert.False(ModelPolicy.ReleaseDefault.Allows(ModelApproval.LicenseReview));
        Assert.True(new ModelPolicy(true).Allows(ModelApproval.LicenseReview));
    }
}

public sealed class StructuredOutputTests
{
    [Fact]
    public void ValidJsonIsParsed()
    {
        Assert.True(StructuredOutput.TryParse<Draft>("{\"text\":\"합성\",\"score\":3,\"tags\":[\"a\"]}", out var draft));
        Assert.Equal("합성", draft.Text);
        Assert.Equal(3, draft.Score);
        Assert.Equal(["a"], draft.Tags);
    }

    [Theory]
    [InlineData("{\"text\":\"합성\",\"score\":3,\"tags\":[],\"extra\":1}")]
    [InlineData("{\"text\":\"합성\",\"tags\":[]}")]
    [InlineData("{\"text\":\"합성\",\"score\":\"3\",\"tags\":[]}")]
    [InlineData("{\"text\":null,\"score\":3,\"tags\":[]}")]
    [InlineData("{\"Text\":\"합성\",\"score\":3,\"tags\":[]}")]
    [InlineData("{\"text\":\"합성\",\"score\":3,\"tags\":[],}")]
    [InlineData("```json\n{\"text\":\"합성\",\"score\":3,\"tags\":[]}\n```")]
    [InlineData("")]
    [InlineData(null)]
    public void AnythingElseIsRejectedWithoutThrowing(string? json) => Assert.False(StructuredOutput.TryParse<Draft>(json, out _));

    [Fact]
    public void SchemaListsEveryMemberAsRequired()
    {
        var schema = System.Text.Json.Nodes.JsonNode.Parse(StructuredOutput.SchemaFor<Draft>())!;

        Assert.Equal("object", (string?)schema["type"]);
        Assert.Equal(["text", "score", "tags"], schema["required"]!.AsArray().Select(n => (string?)n));
    }

    internal sealed record Draft(string Text, int Score, IReadOnlyList<string> Tags);
}

/// <summary>
/// Smoke test against the real Ollama on this PC with a fixed synthetic prompt. Explicit: runs only when
/// requested (<c>dotnet test --project tests/TalkPro.Llm.Tests -- --explicit only</c>).
/// </summary>
public sealed class LiveOllamaSmokeTests
{
    [Fact(Explicit = true)]
    public async Task InstalledRegistryModelAnswersASyntheticPrompt()
    {
        using var client = new OllamaClient(new OllamaOptions(), ModelRegistry.Default, new ModelPolicy(AllowUnapproved: true), NullLogger<OllamaClient>.Instance);

        var status = await client.GetStatusAsync(TestContext.Current.CancellationToken);
        Assert.Equal(RuntimeState.Running, status.State);
        var result = await client.GenerateAsync(
            new LlmRequest("한 문장으로만 답하세요.", OllamaClientTests.Lines("합성 사용자: 오늘 날씨 좋다"), "위 맥락에 어울리는 짧은 한국어 답장을 JSON으로 쓰세요.")
            {
                JsonSchema = StructuredOutput.SchemaFor<Reply>(),
                Temperature = 0,
                Seed = 1,
                MaxOutputTokens = 128,
            },
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.ToString());
        Assert.True(StructuredOutput.TryParse<Reply>(result.Text, out var reply), "structured output did not match the schema");
        Assert.False(string.IsNullOrWhiteSpace(reply.Text));
    }

    internal sealed record Reply(string Text);
}

