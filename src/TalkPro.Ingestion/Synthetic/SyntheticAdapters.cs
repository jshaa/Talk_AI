using TalkPro.Core.Parsing;

namespace TalkPro.Ingestion.Synthetic;

/// <summary>The synthetic test-format adapters (DD-012). Used by tests; not registered in the production container.</summary>
public static class SyntheticAdapters
{
    public static IReadOnlyList<IChatFormatAdapter> All { get; } =
    [
        new SyntheticWindowsAdapter(),
        new SyntheticAndroidAdapter(),
        new SyntheticIosAdapter(),
    ];
}
