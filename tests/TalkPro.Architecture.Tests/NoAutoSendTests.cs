using System.Text.RegularExpressions;

namespace TalkPro.Architecture.Tests;

/// <summary>
/// Static check that the app cannot send messages on the user's behalf (master prompt §25: no auto-send,
/// no unofficial messenger API). Scans every source and XAML file under src/. The Llm module is excluded only
/// for its loopback HTTP client (<c>HttpClient.SendAsync</c> to 127.0.0.1, DD-023).
/// </summary>
public sealed partial class NoAutoSendTests
{
    // Keystroke/window injection, mail, process launching (e.g. messenger URL schemes), native interop, send-named APIs.
    private static readonly string[] ForbiddenTokens =
    [
        "SendKeys", "SendInput", "keybd_event", "mouse_event", "SendMessage", "PostMessage", "SetForegroundWindow",
        "SmtpClient", "System.Net.Mail", "DllImport", "LibraryImport", "Process.Start", "ShellExecute",
        "kakaotalk:", "SendAsync", "AutoSend", "AutoReply",
    ];

    [Fact]
    public void SourcesContainNoSendingOrInputInjection()
    {
        var offenders = SourceFiles()
            .SelectMany(f => ForbiddenTokens.Where(t => File.ReadAllText(f).Contains(t, StringComparison.Ordinal)).Select(t => $"{Relative(f)}: {t}"))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void NoButtonOffersToSend()
    {
        var offenders = SourceFiles()
            .Where(f => f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            .SelectMany(f => SendButton().Matches(File.ReadAllText(f)).Select(m => $"{Relative(f)}: {m.Value}"))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void TheScanSeesTheDraftPipeline()
    {
        // Guards the guard: if the scan silently found no files, the checks above would pass vacuously.
        var names = SourceFiles().Select(Path.GetFileName).ToList();

        Assert.Contains("ReplyDraftService.cs", names);
        Assert.Contains("ReplyDraftViewModel.cs", names);
        Assert.Contains("ReplyDraftView.xaml", names);
        Assert.Contains("WpfClipboardBackend.cs", names);
    }

    private static IEnumerable<string> SourceFiles()
    {
        var src = Path.Combine(RepositoryRoot(), "src");
        var llm = Path.Combine(src, "TalkPro.Llm") + Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.StartsWith(llm, StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TalkPro.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Repository root (TalkPro.sln) not found.");
    }

    private static string Relative(string path) => Path.GetRelativePath(RepositoryRoot(), path);

    [GeneratedRegex(@"<Button[^>]*Content=""[^""]*(전송|보내기|Send)[^""]*""", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex SendButton();
}
