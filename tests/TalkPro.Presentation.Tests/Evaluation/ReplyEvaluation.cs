using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TalkPro.Core.Llm;
using TalkPro.Core.Model;
using TalkPro.Memory;
using TalkPro.Persona.Analysis;
using TalkPro.Persona.Style;
using TalkPro.Presentation.Services;
using TalkPro.Privacy;

namespace TalkPro.Presentation.Tests.Evaluation;

public sealed record EvalPersona(string Id, string Description, string Counterpart, IReadOnlyList<IReadOnlyList<string>> Exchanges);

public sealed record EvalStyle(string Register, string Laugh, string Period, int MaxLength);

public sealed record EvalScenario(string Id, string Category, string Persona, string Incoming, IReadOnlyList<string> Directions, IReadOnlyList<string> Forbidden);

/// <summary>Phase 6.6 Korean reply-draft evaluation set (synthetic only).</summary>
public sealed record EvalSet(
    int Version,
    bool Synthetic,
    string Note,
    IReadOnlyList<EvalPersona> Personas,
    IReadOnlyDictionary<string, EvalStyle> Styles,
    IReadOnlyList<string> CommonForbidden,
    IReadOnlyList<EvalScenario> Scenarios)
{
    public const string FileName = "KoreanReplyEvalSet.v1.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static EvalSet Load() =>
        JsonSerializer.Deserialize<EvalSet>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Evaluation", FileName)), Json)
        ?? throw new InvalidDataException("Evaluation set is empty.");

    public static ReplyDirection ParseDirection(string wire) =>
        Enum.GetValues<ReplyDirection>().Single(d => ReplyDirections.WireName(d) == wire);

    public override string ToString() => $"EvalSet {{ Version = {Version}, Scenarios = {Scenarios.Count} }}";
}

/// <summary>Score of one draft. Contains no text: only a short hash and flags.</summary>
public sealed record DraftScore(int Slot, string Direction, string Hash, int Length, bool DirectionOk, int StyleChecksPassed, int StyleChecks, int ForbiddenHits, int PiiLeaks);

public sealed record ScenarioResult(string Id, string Category, string InputHash, string Error, IReadOnlyList<DraftScore> Drafts, IReadOnlyList<int> MissingSlots, int Requested);

/// <summary>Aggregate metrics (master criteria: direction match, style match, PII leak 0, no auto-send).</summary>
public sealed record EvalReport(
    string Model,
    int Scenarios,
    int Requested,
    int Produced,
    double Coverage,
    double DirectionMatchRate,
    double StyleMatchRate,
    int PiiLeaks,
    int ForbiddenHits,
    bool AutoSendAbsent,
    IReadOnlyList<ScenarioResult> Results)
{
    public string Summary() => string.Create(
        CultureInfo.InvariantCulture,
        $"model={Model} scenarios={Scenarios} coverage={Coverage:P0} direction={DirectionMatchRate:P0} style={StyleMatchRate:P0} piiLeaks={PiiLeaks} forbidden={ForbiddenHits} autoSendAbsent={AutoSendAbsent}");
}

/// <summary>Runs the evaluation set through the real draft pipeline and scores the drafts.</summary>
public static class ReplyEvaluation
{
    private static readonly PiiCategory[] Identifiers = [PiiCategory.HighRiskIdentifier, PiiCategory.Identifier];
    private static readonly JsonSerializerOptions ReportJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<EvalReport> RunAsync(EvalSet set, ILlmClient llm, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(set);
        using var vault = new SyntheticVault();
        var service = new ReplyDraftService(vault.Repository, new ConversationSessionizer(), PrivacyScanner.CreateDefault(), llm);
        var datasets = new Dictionary<string, DatasetId>(StringComparer.Ordinal);
        foreach (var persona in set.Personas)
        {
            datasets[persona.Id] = await vault.ImportAsync([.. persona.Exchanges.SelectMany(e => new[] { (persona.Counterpart, e[0]), ("UserA", e[1]) })]);
        }

        var results = new List<ScenarioResult>();
        string? model = null;
        foreach (var scenario in set.Scenarios)
        {
            var directions = scenario.Directions.Select(EvalSet.ParseDirection).ToList();
            var drafts = await service.DraftAsync(datasets[scenario.Persona], null, scenario.Incoming, directions, cancellationToken);
            model ??= drafts.Model?.ModelId;
            results.Add(Score(set, scenario, drafts, directions.Count));
        }

        var scores = results.SelectMany(r => r.Drafts).ToList();
        var requested = results.Sum(r => r.Requested);
        return new EvalReport(
            model ?? "-",
            results.Count,
            requested,
            scores.Count,
            requested == 0 ? 0 : (double)scores.Count / requested,
            scores.Count == 0 ? 0 : (double)scores.Count(s => s.DirectionOk) / scores.Count,
            scores.Sum(s => s.StyleChecks) == 0 ? 0 : (double)scores.Sum(s => s.StyleChecksPassed) / scores.Sum(s => s.StyleChecks),
            scores.Sum(s => s.PiiLeaks),
            scores.Sum(s => s.ForbiddenHits),
            AutoSendAbsent(),
            results);
    }

    public static ScenarioResult Score(EvalSet set, EvalScenario scenario, DraftSet drafts, int requested)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(drafts);
        var style = set.Styles[scenario.Persona];
        var forbidden = set.CommonForbidden.Concat(scenario.Forbidden).ToList();
        var scores = drafts.Drafts.Select(d =>
        {
            var (passed, total) = StyleChecks(style, d.Text);
            return new DraftScore(
                d.Slot,
                ReplyDirections.WireName(d.Direction),
                Hash(d.Text),
                TextFeatures.Length(d.Text),
                DirectionCheck.Matches(d.Direction, d.Text),
                passed,
                total,
                forbidden.Count(f => d.Text.Contains(f, StringComparison.OrdinalIgnoreCase)),
                PiiLeaks(d.Text, scenario.Incoming));
        }).ToList();
        return new ScenarioResult(scenario.Id, scenario.Category, Hash(scenario.Incoming), drafts.Error.ToString(), scores, drafts.MissingSlots, requested);
    }

    /// <summary>Register, laugh habit, period habit and length against the persona's expected style.</summary>
    public static (int Passed, int Total) StyleChecks(EvalStyle style, string draft)
    {
        ArgumentNullException.ThrowIfNull(style);
        ArgumentNullException.ThrowIfNull(draft);
        var register = TextFeatures.ClassifyRegister(draft);
        var laughs = TextFeatures.LaughRun(draft, 'ㅋ') > 0;
        var period = TextFeatures.EndsWithPeriod(draft);
        bool[] checks =
        [
            register == style.Register switch { "banmal" => Register.Banmal, "haeyo" => Register.Haeyo, "hapsyo" => Register.Hapsyo, _ => Register.Unknown },
            style.Laugh == "often" ? laughs : !laughs,
            style.Period == "none" ? !period : period || draft.TrimEnd().EndsWith('?') || draft.TrimEnd().EndsWith('!'),
            TextFeatures.Length(draft) <= style.MaxLength,
        ];
        return (checks.Count(c => c), checks.Length);
    }

    /// <summary>Identifiers (phone, account, card, e-mail, URL, address…) in a draft that the typed message did not contain.</summary>
    public static int PiiLeaks(string draft, string incoming)
    {
        var message = new ChatMessage(new MessageId("eval"), DateTimeOffset.UnixEpoch, null, MessageKind.Text, draft, 0);
        return PrivacyScanner.CreateDefault().Scan([message], [])
            .Count(f => Identifiers.Contains(f.Category) && !incoming.Contains(draft.Substring(f.Start, f.Length), StringComparison.Ordinal));
    }

    /// <summary>
    /// No-auto-send check on the draft API surface: nothing in TalkPro.Presentation is named like a send
    /// operation (the source-level scan is in TalkPro.Architecture.Tests).
    /// </summary>
    public static bool AutoSendAbsent()
    {
        string[] words = ["Send", "Transmit", "Deliver", "Dispatch", "AutoReply"];
        var names = typeof(ReplyDraftService).Assembly.GetTypes()
            .SelectMany(t => t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Select(m => m.Name).Append(t.Name));
        return !names.Any(n => words.Any(w => n.Contains(w, StringComparison.Ordinal)));
    }

    public static string Hash(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..12];

    /// <summary>Writes the text-free report next to the test binaries.</summary>
    public static string WriteReport(EvalReport report)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "eval-report.json");
        File.WriteAllText(path, JsonSerializer.Serialize(report, ReportJson));
        return path;
    }
}
