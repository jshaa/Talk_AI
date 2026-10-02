namespace TalkPro.Tests.Shared;

/// <summary>Locates checked-in test data by walking up from the test binary to the solution root.</summary>
internal static class RepositoryPaths
{
    public static string Root { get; } = FindRoot();

    /// <summary>The only git-allowed folder for synthetic chat exports (DD-013).</summary>
    public static string GoldenDirectory { get; } = Path.Combine(Root, "tests", "TalkPro.Ingestion.Tests", "Golden");

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TalkPro.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("TalkPro.sln not found above the test output directory.");
    }
}
