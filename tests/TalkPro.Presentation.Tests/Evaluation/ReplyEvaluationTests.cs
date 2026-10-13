using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TalkPro.Core.Llm;
using TalkPro.Llm;
using TalkPro.Llm.Ollama;
using TalkPro.Persona.Style;
using TalkPro.Presentation.Services;

namespace TalkPro.Presentation.Tests.Evaluation;

/// <summary>Checks the evaluation set and the scorer itself. No model needed.</summary>
public sealed class ReplyEvaluationSetTests
{
    private static readonly string[] RequiredCategories = ["약속 잡기", "일정 조율", "거절", "안부", "부탁", "사과", "축하", "농담", "답장 지연"];

    private readonly EvalSet _set = EvalSet.Load();

    [Fact]
    public void SetIsSyntheticCompleteAndConsistent()
    {
        Assert.True(_set.Synthetic);
        Assert.Equal(1, _set.Version);
        Assert.Equal(_set.Scenarios.Count, _set.Scenarios.Select(s => s.Id).Distinct().Count());
        Assert.Empty(RequiredCategories.Except(_set.Scenarios.Select(s => s.Category)));
        Assert.All(_set.Scenarios, s =>
        {
            Assert.Contains(_set.Personas, p => p.Id == s.Persona);
            Assert.True(_set.Styles.ContainsKey(s.Persona));
            Assert.InRange(s.Directions.Count, 1, ReplyPromptBuilder.DraftCount);
            Assert.All(s.Directions, d => EvalSet.ParseDirection(d));
            Assert.False(string.IsNullOrWhiteSpace(s.Incoming));
        });
        Assert.All(_set.Personas, p =>
        {
            Assert.True(p.Exchanges.Count >= 10);
            Assert.All(p.Exchanges, e => Assert.Equal(2, e.Count));
        });
    }

    [Fact]
    public void ScorerMeasuresDirectionStyleForbiddenAndLeaks()
    {
        var scenario = _set.Scenarios.Single(s => s.Id == "pii-01");
        var banmal = _set.Styles["banmal-laugh"];
        var drafts = new DraftSet(
            DraftError.None,
            LlmError.None,
            [
                new DraftItem(0, ReplyDirection.Accept, "네, 내일 연락드릴게요.", 90),
                new DraftItem(1, ReplyDirection.Hold, "확인해 보고 알려드릴게요. 제 번호는 010-9999-0000이에요.", 50),
                new DraftItem(2, ReplyDirection.AskBack, "AI가 보기엔 몇 시가 좋으세요?", 70),
            ],
            [],
            3,
            null);

        var result = ReplyEvaluation.Score(_set, scenario, drafts, 3);

        Assert.Equal([true, true, true], result.Drafts.Select(d => d.DirectionOk));
        Assert.Equal([0, 1, 0], result.Drafts.Select(d => d.PiiLeaks));
        Assert.Equal([0, 0, 1], result.Drafts.Select(d => d.ForbiddenHits));
        Assert.Equal((4, 4), ReplyEvaluation.StyleChecks(banmal, "좋아 ㅋㅋ"));
        Assert.Equal((1, 4), ReplyEvaluation.StyleChecks(banmal, "좋아요."));
        Assert.Equal(0, ReplyEvaluation.PiiLeaks("[전화번호]로 연락할게요", scenario.Incoming));
    }

    [Fact]
    public async Task HarnessRunsEndToEndWithAFakeModelAndReportsNoText()
    {
        var report = await ReplyEvaluation.RunAsync(_set, new ScriptedLlm(), CancellationToken.None);

        Assert.Equal(_set.Scenarios.Count, report.Scenarios);
        Assert.Equal(1.0, report.Coverage);
        Assert.Equal(1.0, report.DirectionMatchRate);
        Assert.Equal(0, report.PiiLeaks);
        Assert.True(report.AutoSendAbsent);
        var json = JsonSerializer.Serialize(report);
        Assert.All(_set.Scenarios, s => Assert.DoesNotContain(s.Incoming, json, StringComparison.Ordinal));
        Assert.DoesNotContain("생각해볼게", json, StringComparison.Ordinal);
    }

    /// <summary>Answers every request with one well-formed draft per requested direction.</summary>
    private sealed class ScriptedLlm : ILlmClient
    {
        public Task<LlmResult> GenerateAsync(LlmRequest request, CancellationToken cancellationToken)
        {
            var lines = request.Instruction.ReplaceLineEndings("\n").Split('\n');
            var wanted = lines.Where(l => l.Length > 3 && char.IsAsciiDigit(l[0]) && l[1] == '.').Select(l => l[3..].Trim()).ToList();
            var drafts = wanted.Select((w, i) => new
            {
                direction = w,
                text = w switch { "accept" => $"좋아 {i}", "hold" => $"생각해볼게 {i}", _ => $"언제 {i}?" },
            });
            return Task.FromResult(new LlmResult(LlmError.None, JsonSerializer.Serialize(new { drafts }), new ModelProvenance("scripted", "d"), TimeSpan.Zero));
        }
    }
}

/// <summary>
/// The Korean quality evaluation against the real Qwen3 8B on this PC. Explicit only:
/// <c>dotnet test --project tests/TalkPro.Presentation.Tests -- --explicit only</c>. The report (bin/…/eval-report.json)
/// and any failure message contain scenario ids, hashes and flags only — never message or draft text.
/// </summary>
public sealed class LiveReplyEvaluationTests
{
    // Initial baselines (Phase 6.6). PII leaks and auto-send are hard rules; the rates are quality floors.
    private const double MinCoverage = 0.8;
    private const double MinDirectionMatch = 0.6;
    private const double MinStyleMatch = 0.6;

    [Fact(Explicit = true)]
    public async Task Qwen3MeetsTheKoreanReplyEvaluation()
    {
        using var llm = new OllamaClient(new OllamaOptions(), ModelRegistry.Default, ModelPolicy.ReleaseDefault, NullLogger<OllamaClient>.Instance);

        var report = await ReplyEvaluation.RunAsync(EvalSet.Load(), llm, TestContext.Current.CancellationToken);
        ReplyEvaluation.WriteReport(report);

        var summary = report.Summary();
        var weak = string.Join(", ", report.Results.Where(r => r.Drafts.Any(d => !d.DirectionOk || d.PiiLeaks > 0) || r.MissingSlots.Count > 0)
            .Select(r => $"{r.Id}#{r.InputHash}"));
        Assert.Equal("qwen3:8b", report.Model);
        Assert.True(report.PiiLeaks == 0, summary);
        Assert.True(report.AutoSendAbsent, summary);
        Assert.True(report.Coverage >= MinCoverage, $"{summary} | {weak}");
        Assert.True(report.DirectionMatchRate >= MinDirectionMatch, $"{summary} | {weak}");
        Assert.True(report.StyleMatchRate >= MinStyleMatch, $"{summary} | {weak}");
    }
}
