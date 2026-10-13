using TalkPro.Core.Parsing;
using TalkPro.Ingestion.Formats.Synthetic;

namespace TalkPro.Tools.FormatProbe;

/// <summary>
/// Usage:
///   TalkPro.FormatProbe profile  &lt;export.txt&gt; [more files…]
///   TalkPro.FormatProbe validate &lt;export.txt&gt; [more files…]
/// Reads the given files, prints a content-free report to standard output. Never writes files,
/// never prints file names or paths, never uses the network.
/// </summary>
internal static class Program
{
    private const long MaxFileBytes = 512L * 1024 * 1024;

    private static int Main(string[] args)
    {
        if (args.Length < 2 || args[0] is not ("profile" or "validate"))
        {
            Console.Error.WriteLine("Usage: TalkPro.FormatProbe profile|validate <export.txt> [more files...]");
            return 2;
        }

        var files = new List<ReadOnlyMemory<byte>>();
        for (var i = 1; i < args.Length; i++)
        {
            var info = new FileInfo(args[i]);
            if (!info.Exists || info.Length > MaxFileBytes)
            {
                Console.Error.WriteLine($"Argument #{i}: file missing or larger than 512 MB.");
                return 2;
            }

            files.Add(File.ReadAllBytes(info.FullName));
        }

        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.Write(args[0] == "profile"
            ? ProbeReport.Profile(files)
            : ProbeReport.Validate(files, Adapters.All, ParseContext.ForLocalTimeZone()));
        return 0;
    }
}

/// <summary>Adapters known to the probe: synthetic test formats plus real formats once added (Phase 1.8, step 2).</summary>
public static class Adapters
{
    public static IReadOnlyList<IChatFormatAdapter> All { get; } = [.. TalkPro.Ingestion.Formats.Kakao.KakaoFormats.All, .. SyntheticFormats.All];
}
