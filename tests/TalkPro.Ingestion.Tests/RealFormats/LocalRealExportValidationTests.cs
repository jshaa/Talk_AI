using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TalkPro.Core.Parsing;
using TalkPro.Ingestion.Import;
using TalkPro.Ingestion.RealFormats;
using TalkPro.Tests.Shared;

namespace TalkPro.Ingestion.Tests.RealFormats;

/// <summary>
/// LOCAL / PRIVATE integration validation against real exports. Opt-in only: skipped unless
/// <see cref="DirectoryVariable"/> names a folder outside the repository. Files are read in place,
/// never copied. Output contains only file ordinal, a hash prefix, format id, line numbers,
/// outcomes, issues and counts — never paths, file names, sender labels or message text.
/// <code>
/// set TALKPRO_REAL_EXPORTS_DIR=D:\private\exports
/// dotnet test --project tests/TalkPro.Ingestion.Tests -- --filter-trait "Category=LocalPrivateValidation"
/// </code>
/// </summary>
[Trait("Category", Category)]
public sealed class LocalRealExportValidationTests
{
    public const string Category = "LocalPrivateValidation";

    public const string DirectoryVariable = "TALKPRO_REAL_EXPORTS_DIR";

    [Fact]
    public void LocalRealExportsParseWithoutIssues()
    {
        var directory = Environment.GetEnvironmentVariable(DirectoryVariable);
        Assert.SkipWhen(string.IsNullOrWhiteSpace(directory), "Local private validation is opt-in: set " + DirectoryVariable + " to a folder outside the repository.");

        // Messages below deliberately omit the path: it usually contains the Windows user name.
        Assert.True(Directory.Exists(directory), DirectoryVariable + " does not point to an existing directory.");
        Assert.False(IsInsideRepository(directory!), DirectoryVariable + " must point outside the repository.");

        var files = Directory.EnumerateFiles(directory!, "*.txt", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal).ToList();
        Assert.True(files.Count > 0, DirectoryVariable + " contains no .txt exports.");

        var parser = new ChatExportParser(RealFormatAdapters.All);
        var context = ParseContext.ForLocalTimeZone();
        var lines = new List<string>();
        var allClean = true;
        for (var i = 0; i < files.Count; i++)
        {
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(files[i]);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The exception message would contain the path; only its type is reported.
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"file #{i + 1}: unreadable ({ex.GetType().Name})"));
                allClean = false;
                continue;
            }

            var result = parser.Parse(bytes, context);
            allClean &= result.Chat is { Issues.Count: 0 };
            lines.Add(RealExportReport.Describe(i + 1, SHA256.HashData(bytes), result));
        }

        Assert.True(allClean, "Real-export validation found problems:\n" + string.Join('\n', lines));
    }

    private static bool IsInsideRepository(string directory)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(RepositoryPaths.Root)) + Path.DirectorySeparatorChar;
        var candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Content-free description of one validation result (also used to test that it stays content-free).</summary>
internal static class RealExportReport
{
    public const int MaxIssuesPerFile = 50;

    public static string Describe(int ordinal, byte[] sha256, ChatImportResult result)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"file #{ordinal} (sha256 {Convert.ToHexStringLower(sha256)[..12]}): {result.Status}");
        if (result.Chat is not { } chat)
        {
            return text.ToString();
        }

        var s = chat.Statistics;
        text.Append(CultureInfo.InvariantCulture, $" format={chat.FormatId} encoding={chat.Encoding} lines={s.TotalLines} messages={s.MessagesStarted} system={s.SystemMessages} continuations={s.Continuations} metadata={s.Metadata} ignored={s.Ignored} malformed={s.Malformed} unsupported={s.Unsupported} participants={chat.Participants.Count}");
        foreach (var issue in chat.Issues.Take(MaxIssuesPerFile))
        {
            text.Append(CultureInfo.InvariantCulture, $"\n  line {issue.LineNumber}: {issue.Outcome} {issue.Issue}");
        }

        if (chat.Issues.Count > MaxIssuesPerFile)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n  … {chat.Issues.Count - MaxIssuesPerFile} more");
        }

        return text.ToString();
    }
}
