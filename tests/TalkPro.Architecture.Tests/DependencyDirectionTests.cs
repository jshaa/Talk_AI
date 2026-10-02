using System.Reflection;

namespace TalkPro.Architecture.Tests;

/// <summary>
/// Enforces the dependency rules of report ch. 11 / master prompt §21:
/// everything points to Core, feature modules never depend on each other, and only the LLM
/// module may use network APIs (localhost model runtime).
/// </summary>
public sealed class DependencyDirectionTests
{
    private const string CoreName = "TalkPro.Core";

    private static readonly string[] FeatureModules =
    [
        "TalkPro.Ingestion",
        "TalkPro.Privacy",
        "TalkPro.Memory",
        "TalkPro.Persona",
        "TalkPro.Llm",
        "TalkPro.Consent",
        "TalkPro.Security",
    ];

    private static readonly string[] NetworkAssemblies =
    [
        "System.Net.Http",
        "System.Net.Sockets",
        "System.Net.Requests",
        "System.Net.WebClient",
        "System.Net.WebSockets",
        "System.Net.WebSockets.Client",
    ];

    public static TheoryData<string> FeatureModuleNames => [.. FeatureModules];

    [Fact]
    public void CoreReferencesOnlyTheBaseClassLibrary()
    {
        var references = ReferencedNames(Load(CoreName));

        Assert.All(references, name => Assert.True(
            name.StartsWith("System", StringComparison.Ordinal) || name == "netstandard",
            $"TalkPro.Core must not reference '{name}'."));
    }

    [Theory]
    [MemberData(nameof(FeatureModuleNames))]
    public void FeatureModuleDependsOnlyOnCore(string module)
    {
        var projectReferences = ReferencedNames(Load(module)).Where(IsTalkPro).ToList();

        Assert.All(projectReferences, name => Assert.Equal(CoreName, name));
    }

    [Fact]
    public void InfrastructureDependsOnlyOnCoreAndSecurity()
    {
        var projectReferences = ReferencedNames(Load("TalkPro.Infrastructure")).Where(IsTalkPro).ToList();

        Assert.All(projectReferences, name => Assert.Contains(name, new[] { CoreName, "TalkPro.Security" }));
    }

    [Fact]
    public void OnlyTheLlmModuleMayUseNetworkApis()
    {
        var assemblies = FeatureModules.Where(m => m != "TalkPro.Llm").Append(CoreName).Append("TalkPro.Infrastructure");

        foreach (var assembly in assemblies)
        {
            var network = ReferencedNames(Load(assembly)).Intersect(NetworkAssemblies, StringComparer.Ordinal).ToList();
            Assert.True(network.Count == 0, $"{assembly} references network assemblies: {string.Join(", ", network)}");
        }
    }

    private static Assembly Load(string name) => Assembly.Load(new AssemblyName(name));

    private static List<string> ReferencedNames(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies().Select(r => r.Name!)];

    private static bool IsTalkPro(string name) => name.StartsWith("TalkPro.", StringComparison.Ordinal);
}
