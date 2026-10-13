using TalkPro.Ingestion.Speakers;

namespace TalkPro.Ingestion.Tests.Speakers;

/// <summary>The browser runtime (web viewer) has no NFKC; its hand-folded path must give the same comparison keys.</summary>
public sealed class NameNormalizationTests
{
    [Theory]
    [InlineData("User A", "USERA")]
    [InlineData("Ｕｓｅｒ　Ａ", "USERA")]
    [InlineData("ｕｓｅｒ_a!", "USERA")]
    [InlineData("가나다 (회사)", "가나다회사")]
    [InlineData("가나다１２", "가나다12")]
    public void BrowserAndDesktopPathsAgree(string name, string expected)
    {
        Assert.Equal(expected, SuspicionDetector.Normalize(name, compatibilityFormAvailable: true));
        Assert.Equal(expected, SuspicionDetector.Normalize(name, compatibilityFormAvailable: false));
    }
}
