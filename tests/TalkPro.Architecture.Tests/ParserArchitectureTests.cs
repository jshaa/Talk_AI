using System.Reflection;
using System.Runtime.CompilerServices;

namespace TalkPro.Architecture.Tests;

/// <summary>Phase 1 rules: I/O-free Core contracts, content-free ToString, no parser logging, independent generator.</summary>
public sealed class ParserArchitectureTests
{
    private const BindingFlags AllMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly string[] NetworkAssemblies =
        ["System.Net.Http", "System.Net.Sockets", "System.Net.Requests", "System.Net.WebClient", "System.Net.WebSockets", "System.Net.WebSockets.Client"];

    [Fact]
    public void CorePublicApiHasNoIoTypes()
    {
        var offenders = Load("TalkPro.Core").GetExportedTypes()
            .SelectMany(t => SignatureTypes(t).Select(s => (Owner: t, Used: s)))
            .Where(x => x.Used.Namespace == "System.IO")
            .Select(x => $"{x.Owner.Name} uses {x.Used.Name}")
            .Distinct()
            .ToList();

        Assert.Empty(offenders);
    }

    [Theory]
    [InlineData("TalkPro.Core")]
    [InlineData("TalkPro.Ingestion")]
    [InlineData("TalkPro.Privacy")]
    [InlineData("TalkPro.Memory")]
    [InlineData("TalkPro.Persona")]
    [InlineData("TalkPro.Llm")]
    [InlineData("TalkPro.Presentation")]
    [InlineData("TalkPro.Web")]
    public void RecordsNeverUseTheCompilerGeneratedToString(string assembly)
    {
        // Compiler-generated record ToString prints every member, i.e. message text and names.
        var offenders = Load(assembly).GetTypes()
            .Where(IsRecord)
            .Where(t => t.GetMethod(nameof(ToString), BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes) is not { } m
                        || m.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            .Select(t => t.FullName)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void IngestionDoesNotLog()
    {
        var references = Load("TalkPro.Ingestion").GetReferencedAssemblies().Select(a => a.Name!);

        Assert.DoesNotContain(references, n => n.StartsWith("Microsoft.Extensions.Logging", StringComparison.Ordinal));
    }

    [Fact]
    public void SampleGeneratorIsAnIndependentOfflineOracle()
    {
        var references = Load("TalkPro.SampleGenerator").GetReferencedAssemblies().Select(a => a.Name!).ToList();

        Assert.DoesNotContain(references, n => n.StartsWith("TalkPro.", StringComparison.Ordinal));
        Assert.Empty(references.Intersect(NetworkAssemblies, StringComparer.Ordinal));
    }

    [Fact]
    public void FormatProbeIsOfflineAndStorageFree()
    {
        // DD-021: the probe reads real exports, so it must not reach the network or the vault/infrastructure.
        var references = Load("TalkPro.FormatProbe").GetReferencedAssemblies().Select(a => a.Name!).ToList();

        Assert.Empty(references.Intersect(NetworkAssemblies, StringComparer.Ordinal));
        Assert.DoesNotContain("TalkPro.Infrastructure", references);
        Assert.DoesNotContain(references, n => n.StartsWith("Microsoft.Extensions.Logging", StringComparison.Ordinal));
    }

    [Fact]
    public void PresentationIsUiToolkitAndNetworkFree()
    {
        // ViewModels must stay reusable by a future MAUI client (DD-014, DD-019).
        string[] forbidden = ["PresentationFramework", "PresentationCore", "WindowsBase", "System.Xaml", .. NetworkAssemblies];
        var references = Load("TalkPro.Presentation").GetReferencedAssemblies().Select(a => a.Name!).ToList();

        Assert.Empty(references.Intersect(forbidden, StringComparer.Ordinal));
    }

    [Fact]
    public void WebViewerHasNoStorageKeysAiOrSharedUiLayer()
    {
        // DD-026: the iPhone web viewer processes a chat in the browser's memory only.
        var references = Load("TalkPro.Web").GetReferencedAssemblies().Select(a => a.Name!).ToList();

        Assert.Empty(references.Intersect(
            ["TalkPro.Infrastructure", "TalkPro.Security", "TalkPro.Llm", "TalkPro.Presentation", "Microsoft.Data.Sqlite"],
            StringComparer.Ordinal));
    }

    [Fact]
    public void OnlyPrivacyCanCreateRedactedText()
    {
        // DD-023: conversation data reaches a model only as RedactedText, which only the redaction view creates.
        var type = typeof(TalkPro.Core.Llm.RedactedText);
        var friends = Load("TalkPro.Core").GetCustomAttributes<InternalsVisibleToAttribute>().Select(a => a.AssemblyName).ToList();

        Assert.Empty(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.True(type.IsSealed);
        Assert.Equal(["TalkPro.Privacy"], friends);
    }

    private static Assembly Load(string name) => Assembly.Load(new AssemblyName(name));

    private static bool IsRecord(Type type) =>
        type.GetMethod("PrintMembers", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly) is not null
        || type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.Instance) is not null;

    private static IEnumerable<Type> SignatureTypes(Type type)
    {
        foreach (var member in type.GetMembers(AllMembers).Where(m => m is not MethodBase { IsPrivate: true } and not FieldInfo { IsPrivate: true }))
        {
            switch (member)
            {
                case MethodBase method:
                    foreach (var p in method.GetParameters())
                    {
                        yield return Unwrap(p.ParameterType);
                    }

                    if (method is MethodInfo info)
                    {
                        yield return Unwrap(info.ReturnType);
                    }

                    break;
                case PropertyInfo property:
                    yield return Unwrap(property.PropertyType);
                    break;
                case FieldInfo field:
                    yield return Unwrap(field.FieldType);
                    break;
            }
        }
    }

    private static Type Unwrap(Type type) => type.HasElementType ? Unwrap(type.GetElementType()!) : type;
}
