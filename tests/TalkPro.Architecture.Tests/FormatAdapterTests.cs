using System.Reflection;
using TalkPro.Core.Parsing;
using TalkPro.Ingestion.RealFormats;
using TalkPro.Ingestion.Synthetic;

namespace TalkPro.Architecture.Tests;

/// <summary>Shape rules for every format adapter in Ingestion (DD-009, DD-012, DD-014).</summary>
public sealed class FormatAdapterTests
{
    private static readonly Type[] ForbiddenMemberTypes =
    [
        typeof(Stream), typeof(TextReader), typeof(TextWriter), typeof(FileSystemInfo), typeof(IServiceProvider),
    ];

    private static readonly Type[] Adapters =
        [.. typeof(RealFormatAdapters).Assembly.GetTypes().Where(t => !t.IsAbstract && typeof(IChatFormatAdapter).IsAssignableFrom(t))];

    public static TheoryData<Type> AdapterTypes => [.. Adapters];

    [Theory]
    [MemberData(nameof(AdapterTypes))]
    public void AdaptersHoldNoIoLoggingOrMutableState(Type adapter)
    {
        Assert.True(adapter.IsSealed, adapter.Name);
        Assert.All(adapter.GetConstructors(), c => Assert.Empty(c.GetParameters()));

        var fields = HierarchyFields(adapter).ToList();
        Assert.All(fields, f => Assert.True(f.IsInitOnly || f.IsLiteral, $"{adapter.Name}.{f.Name} is mutable"));
        Assert.DoesNotContain(fields, f => ForbiddenMemberTypes.Any(t => t.IsAssignableFrom(f.FieldType)));
        Assert.DoesNotContain(fields, f => f.FieldType.Namespace?.StartsWith("Microsoft.Extensions.Logging", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void RealAndSyntheticAdaptersAreSeparate()
    {
        Assert.All(RealFormatAdapters.All, a =>
        {
            Assert.IsAssignableFrom<RealExportAdapterBase>(a);
            Assert.False(a is SyntheticAdapterBase);
            Assert.StartsWith("kakaotalk.", a.FormatId.Value, StringComparison.Ordinal);
        });
        Assert.All(SyntheticAdapters.All, a => Assert.StartsWith("synthetic.", a.FormatId.Value, StringComparison.Ordinal));
        Assert.Empty(RealFormatAdapters.All.Select(a => a.FormatId).Intersect(SyntheticAdapters.All.Select(a => a.FormatId)));
    }

    [Fact]
    public void EveryAdapterIsListedInExactlyOneRegistry()
    {
        var listed = RealFormatAdapters.All.Concat(SyntheticAdapters.All).Select(a => a.GetType()).ToList();

        Assert.Equal(Adapters.OrderBy(t => t.FullName, StringComparer.Ordinal), listed.OrderBy(t => t.FullName, StringComparer.Ordinal));
    }

    private static IEnumerable<FieldInfo> HierarchyFields(Type type)
    {
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                yield return field;
            }
        }
    }
}
