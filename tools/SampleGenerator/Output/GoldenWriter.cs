using System.Text.RegularExpressions;

namespace TalkPro.Tools.SampleGenerator.Output;

/// <summary>
/// Writes a <see cref="GoldenSet"/> to disk. Only a directory named <c>Golden</c> is accepted (the
/// only git-allowed location for chat-like text, DD-013). Files are only written, never read; stale
/// files are recognised by the generator's own naming scheme and nothing else is touched.
/// </summary>
public static partial class GoldenWriter
{
    public const string GoldenDirectoryName = "Golden";

    public static bool IsGoldenDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        return string.Equals(Path.GetFileName(full), GoldenDirectoryName, StringComparison.Ordinal);
    }

    /// <returns>Number of stale generator files deleted.</returns>
    public static int Write(GoldenSet set, string directory)
    {
        ArgumentNullException.ThrowIfNull(set);
        if (!IsGoldenDirectory(directory))
        {
            throw new ArgumentException("Output directory must be named 'Golden'.", nameof(directory));
        }

        Directory.CreateDirectory(directory);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in set.Files)
        {
            File.WriteAllBytes(Path.Combine(directory, file.Name), file.Content);
            names.Add(file.Name);
        }

        File.WriteAllBytes(Path.Combine(directory, GoldenSet.ManifestFileName), set.Manifest);

        var deleted = 0;
        foreach (var path in Directory.EnumerateFiles(directory))
        {
            var name = Path.GetFileName(path);
            if (GeneratedName().IsMatch(name) && !names.Contains(name))
            {
                File.Delete(path);
                deleted++;
            }
        }

        return deleted;
    }

    [GeneratedRegex(@"^(w1|a1|i1)\.[a-z0-9-]+\.(utf8|utf8bom|cp949)\.txt$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    public static partial Regex GeneratedName();
}
