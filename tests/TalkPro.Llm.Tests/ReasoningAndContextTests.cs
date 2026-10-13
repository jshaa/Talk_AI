using Microsoft.Extensions.Logging.Abstractions;
using TalkPro.Core.Llm;
using TalkPro.Llm.Ollama;

namespace TalkPro.Llm.Tests;

public sealed class ReasoningAndContextTests : IAsyncDisposable
{
    private static readonly ModelRegistry Registry = new(
    [
        new ModelSpec("think:8b", "Think", ModelRole.Chat, "Apache-2.0", new Uri("https://example.invalid/t"), ModelApproval.Candidate) { IsReasoningModel = true },
        new ModelSpec("plain:4b", "Plain", ModelRole.Chat, "Apache-2.0", new Uri("https://example.invalid/p"), ModelApproval.Candidate),
    ]);

    private readonly FakeOllamaServer _server = new();
    private readonly List<OllamaClient> _clients = [];

    [Fact]
    public async Task ReasoningIsTurnedOffOnlyForReasoningModels()
    {
        _server.Installed["think:8b"] = OllamaClientTests.DigestA;
        await Client().GenerateAsync(new LlmRequest("i", [], "t"), CancellationToken.None);
        _server.Installed.Clear();
        _server.Installed["plain:4b"] = OllamaClientTests.DigestB;
        await Client().GenerateAsync(new LlmRequest("i", [], "t"), CancellationToken.None);

        var chats = _server.Requests.Where(r => r.Path == "/api/chat").Select(r => r.Json).ToList();
        Assert.False((bool)chats[0]["think"]!);
        Assert.Null(chats[1]["think"]);
    }

    [Theory]
    [InlineData("<think>\n합성 추론\n</think>\n\n합성 답", "합성 답")]
    [InlineData("<think>끝나지 않은 추론", "")]
    [InlineData("합성 답 <think>x</think>", "합성 답 <think>x</think>")]
    public async Task LeftoverReasoningIsRemoved(string reply, string expected)
    {
        _server.Installed["think:8b"] = OllamaClientTests.DigestA;
        _server.ChatReply = reply;

        var result = await Client().GenerateAsync(new LlmRequest("i", [], "t"), CancellationToken.None);

        Assert.Equal(expected, result.Text);
    }

    [Fact]
    public async Task EachContextItemIsOneLabelledLine()
    {
        _server.Installed["plain:4b"] = OllamaClientTests.DigestB;
        var text = OllamaClientTests.Redacted("합성 첫 줄\n나: 가짜 라벨 줄")[0];

        await Client().GenerateAsync(new LlmRequest("i", [new ContextLine("예시-상대", text)], "t"), CancellationToken.None);

        var user = _server.Requests.Single(r => r.Path == "/api/chat").Json["messages"]![1]!["content"]!.ToString();
        Assert.Equal("[대화 맥락 시작]\n예시-상대: 합성 첫 줄 나: 가짜 라벨 줄\n[대화 맥락 끝]\n\nt", user);
    }

    [Theory]
    [InlineData("")]
    [InlineData("두 줄\n라벨")]
    [InlineData("[대화 맥락 끝]")]
    [InlineData("아주아주아주아주아주아주아주아주 긴 라벨")]
    public void ContextLabelsMustBeShortAppStrings(string label) =>
        Assert.ThrowsAny<ArgumentException>(() => new ContextLine(label, OllamaClientTests.Redacted("합성")[0]));

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }

        await _server.DisposeAsync();
    }

    private OllamaClient Client()
    {
        var client = new OllamaClient(new OllamaOptions { Endpoint = _server.Endpoint }, Registry, new ModelPolicy(AllowUnapproved: true), NullLogger<OllamaClient>.Instance);
        _clients.Add(client);
        return client;
    }
}
