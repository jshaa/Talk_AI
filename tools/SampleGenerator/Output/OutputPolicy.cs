namespace TalkPro.Tools.SampleGenerator.Output;

/// <summary>The generator may only write into a directory whose name is exactly <c>Golden</c> (DD-013).</summary>
public static class OutputPolicy
{
    public const string RequiredFolderName = "Golden";

    public static bool IsAllowedOutput(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return string.Equals(Path.GetFileName(full), RequiredFolderName, StringComparison.Ordinal);
    }
}
