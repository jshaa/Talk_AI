using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace TalkPro.Architecture.Tests;

/// <summary>
/// Checks which external members an assembly calls, from its metadata (no execution):
/// the SampleGenerator never reads files or uses the network (DD-013), and no assembly registers a
/// global encoding provider (DD-011).
/// </summary>
public sealed class MemberReferenceTests
{
    private const string SampleGenerator = "TalkPro.SampleGenerator";

    private static readonly string[] TalkProAssemblies =
    [
        "TalkPro.Core", "TalkPro.Ingestion", "TalkPro.Privacy", "TalkPro.Memory", "TalkPro.Persona",
        "TalkPro.Llm", "TalkPro.Consent", "TalkPro.Security", "TalkPro.Infrastructure", SampleGenerator,
    ];

    private static readonly string[] FileReadingTypes =
    [
        "System.IO.StreamReader", "System.IO.FileStream", "System.IO.FileInfo", "System.IO.BinaryReader", "System.IO.TextReader",
    ];

    public static TheoryData<string> AssemblyNames => [.. TalkProAssemblies];

    [Fact]
    public void SampleGeneratorIsAnIndependentOracle()
    {
        var references = Assembly.Load(new AssemblyName(SampleGenerator)).GetReferencedAssemblies().Select(r => r.Name!).ToList();

        Assert.DoesNotContain(references, name => name.StartsWith("TalkPro.", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.StartsWith("System.Net", StringComparison.Ordinal));
    }

    [Fact]
    public void SampleGeneratorNeverReadsFiles()
    {
        var reads = MemberReferences(SampleGenerator)
            .Where(m => FileReadingTypes.Contains(m.Type, StringComparer.Ordinal)
                || (m.Type == "System.IO.File" && (m.Name.StartsWith("Read", StringComparison.Ordinal) || m.Name.StartsWith("Open", StringComparison.Ordinal) || m.Name is "Copy" or "Move" or "Replace")))
            .ToList();

        Assert.Empty(reads);
        Assert.Contains(MemberReferences(SampleGenerator), m => m is { Type: "System.IO.File", Name: "WriteAllBytes" });
    }

    [Theory]
    [MemberData(nameof(AssemblyNames))]
    public void NoAssemblyRegistersAGlobalEncodingProvider(string assembly)
    {
        Assert.DoesNotContain(MemberReferences(assembly), m => m is { Type: "System.Text.Encoding", Name: "RegisterProvider" });
    }

    private static List<(string Type, string Name)> MemberReferences(string assemblyName)
    {
        var location = Assembly.Load(new AssemblyName(assemblyName)).Location;
        using var stream = File.OpenRead(location);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();

        var result = new List<(string, string)>();
        foreach (var handle in metadata.MemberReferences)
        {
            var member = metadata.GetMemberReference(handle);
            var type = member.Parent.Kind == HandleKind.TypeReference
                ? FullName(metadata, (TypeReferenceHandle)member.Parent)
                : string.Empty;
            result.Add((type, metadata.GetString(member.Name)));
        }

        return result;
    }

    private static string FullName(MetadataReader metadata, TypeReferenceHandle handle)
    {
        var type = metadata.GetTypeReference(handle);
        var ns = metadata.GetString(type.Namespace);
        var name = metadata.GetString(type.Name);
        return ns.Length == 0 ? name : ns + "." + name;
    }
}
