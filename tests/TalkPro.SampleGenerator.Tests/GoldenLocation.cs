namespace TalkPro.SampleGenerator.Tests;

internal static class GoldenLocation
{
    /// <summary>The committed golden folder in the source tree (found by walking up to TalkPro.sln).</summary>
    public static string Directory
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "TalkPro.sln")))
                {
                    return Path.Combine(dir.FullName, "tests", "TalkPro.Ingestion.Tests", "Golden");
                }
            }

            throw new InvalidOperationException("Repository root (TalkPro.sln) not found.");
        }
    }

    public static string PathOf(string relativePath) =>
        Path.Combine(Directory, relativePath.Replace('/', Path.DirectorySeparatorChar));
}
