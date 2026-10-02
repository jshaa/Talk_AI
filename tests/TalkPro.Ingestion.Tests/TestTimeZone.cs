using TalkPro.Core.Parsing;

namespace TalkPro.Ingestion.Tests;

internal static class TestTimeZone
{
    /// <summary>Fixed +09:00 zone so results never depend on the machine's time-zone database.</summary>
    public static readonly TimeZoneInfo Plus9 = TimeZoneInfo.CreateCustomTimeZone("TalkPro-Test+09", TimeSpan.FromHours(9), "Test +09", "Test +09");

    public static ParseContext Context() => new() { TimeZone = Plus9 };
}
