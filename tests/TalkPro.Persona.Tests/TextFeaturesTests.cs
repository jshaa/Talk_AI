using TalkPro.Persona.Analysis;

namespace TalkPro.Persona.Tests;

public sealed class TextFeaturesTests
{
    [Theory]
    [InlineData("감사합니다", Register.Hapsyo)]
    [InlineData("알겠습니다.", Register.Hapsyo)]
    [InlineData("내일 갑니까?", Register.Hapsyo)]
    [InlineData("확인해 주십시오", Register.Hapsyo)]
    [InlineData("좋아요~", Register.Haeyo)]
    [InlineData("그렇죠", Register.Haeyo)]
    [InlineData("그래 ㅋㅋ", Register.Banmal)]
    [InlineData("바쁘니까 나중에 보자", Register.Banmal)]
    [InlineData("그러니까", Register.Banmal)]
    [InlineData("그건 아니다", Register.Banmal)]
    [InlineData("ㅋㅋㅋ", Register.Unknown)]
    [InlineData("👍", Register.Unknown)]
    [InlineData("OK 123", Register.Unknown)]
    public void RegisterFromSentenceEnding(string text, Register expected) => Assert.Equal(expected, TextFeatures.ClassifyRegister(text));

    [Theory]
    [InlineData("밥 먹었어?", true)]
    [InlineData("이거 어때 ㅋㅋ", true)]
    [InlineData("갈까", true)]
    [InlineData("알겠어", false)]
    public void Questions(string text, bool expected) => Assert.Equal(expected, TextFeatures.IsQuestion(text));

    [Fact]
    public void SurfaceFeatures()
    {
        Assert.Equal(4, TextFeatures.LaughRun("아 ㅋㅋ 진짜 ㅋㅋㅋㅋ", 'ㅋ'));
        Assert.Equal(0, TextFeatures.LaughRun("아 진짜", 'ㅎ'));
        Assert.Equal(["😀", "👍"], TextFeatures.Emojis("좋아 😀 👍🏻"));
        Assert.True(TextFeatures.EndsWithPeriod("알겠어."));
        Assert.False(TextFeatures.EndsWithPeriod("음..."));
        Assert.True(TextFeatures.HasEllipsis("음…"));
        Assert.True(TextFeatures.HasTilde("좋아~"));
        Assert.True(TextFeatures.HasExclamation("와!"));
        Assert.Equal(3, TextFeatures.Length("가😀나"));
    }

    [Fact]
    public void KeywordsDropPseudonymTokensParticlesStopWordsAndLaughter()
    {
        string[] texts =
        [
            "[인물1]이랑 영화 봤어",
            "영화가 진짜 재밌었어 ㅋㅋㅋ",
            "[인물1] 만나서 영화를 또 봤어",
            "그냥 그래 ㅠㅠ",
        ];

        var top = KeywordExtractor.Top(texts, 10);

        Assert.Equal(("영화", 3), top[0]);
        Assert.DoesNotContain(top, k => k.Word.Contains("인물", StringComparison.Ordinal));
        Assert.DoesNotContain(top, k => k.Word is "그냥" or "진짜" or "ㅋㅋㅋ" or "ㅠㅠ");
    }
}
