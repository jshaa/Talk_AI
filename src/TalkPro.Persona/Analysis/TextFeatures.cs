using System.Globalization;
using System.Text;

namespace TalkPro.Persona.Analysis;

/// <summary>Speech level of a message, from its sentence ending (rule-based, no LLM).</summary>
public enum Register
{
    /// <summary>합쇼체: -습니다, -ㅂ니다, -습니까, -ㅂ니까, -십시오.</summary>
    Hapsyo,

    /// <summary>해요체: -요, -죠.</summary>
    Haeyo,

    /// <summary>반말: other Hangul endings.</summary>
    Banmal,

    /// <summary>No Hangul ending (emoji, laughter, numbers, Latin only).</summary>
    Unknown,
}

/// <summary>Measurable surface features of one message (master prompt §9 "Measured Features").</summary>
public static class TextFeatures
{
    /// <summary>Index of final consonant ㅂ in a precomposed Hangul syllable ((code − 가) mod 28).</summary>
    private const int FinalBieup = 17;

    public static int Length(string text) => new StringInfo(text ?? string.Empty).LengthInTextElements;

    public static Register ClassifyRegister(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var word = LastHangulWord(text);
        if (word is null)
        {
            return Register.Unknown;
        }

        if (IsHapsyo(word))
        {
            return Register.Hapsyo;
        }

        return word.EndsWith('요') || word.EndsWith('죠') ? Register.Haeyo : Register.Banmal;
    }

    public static bool IsQuestion(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var core = text.TrimEnd(' ', '~', '.', '!', 'ㅋ', 'ㅎ');
        return text.Contains('?', StringComparison.Ordinal)
            || core.EndsWith('냐') || core.EndsWith('까') || core.EndsWith("나요", StringComparison.Ordinal) || core.EndsWith("어때", StringComparison.Ordinal);
    }

    /// <summary>Longest run of <paramref name="laugh"/> (e.g. 'ㅋ'), 0 if none.</summary>
    public static int LaughRun(string text, char laugh) => LongestRun(text, laugh);

    public static IEnumerable<string> Emojis(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach (var rune in text.EnumerateRunes())
        {
            if (IsEmoji(rune))
            {
                yield return rune.ToString();
            }
        }
    }

    public static bool EndsWithPeriod(string text) => text.TrimEnd().EndsWith('.') && !text.TrimEnd().EndsWith("..", StringComparison.Ordinal);

    public static bool HasEllipsis(string text) => text.Contains("...", StringComparison.Ordinal) || text.Contains('…', StringComparison.Ordinal);

    public static bool HasTilde(string text) => text.Contains('~', StringComparison.Ordinal);

    public static bool HasExclamation(string text) => text.Contains('!', StringComparison.Ordinal);

    /// <summary>
    /// -십시오, or -니다/-니까 right after a syllable with final ㅂ (습니다, 갑니다, 합니까).
    /// Plain -니까/-니다 without ㅂ is a banmal connective or plain form (그러니까, 아니다).
    /// </summary>
    private static bool IsHapsyo(string word)
    {
        if (word.EndsWith("십시오", StringComparison.Ordinal))
        {
            return true;
        }

        if (word.Length < 3 || !(word.EndsWith("니다", StringComparison.Ordinal) || word.EndsWith("니까", StringComparison.Ordinal)))
        {
            return false;
        }

        return (word[^3] - '가') % 28 == FinalBieup;
    }

    private static bool IsEmoji(Rune rune) =>
        rune.Value is >= 0x1F300 and <= 0x1FAFF or >= 0x2600 and <= 0x27BF
        && Rune.GetUnicodeCategory(rune) is UnicodeCategory.OtherSymbol;

    private static string? LastHangulWord(string text)
    {
        var words = text.Split([' ', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries);
        for (var i = words.Length - 1; i >= 0; i--)
        {
            var hangul = new StringBuilder();
            foreach (var c in words[i])
            {
                if (c is >= '가' and <= '힣')
                {
                    hangul.Append(c);
                }
            }

            if (hangul.Length > 0)
            {
                return hangul.ToString();
            }
        }

        return null;
    }

    private static int LongestRun(string text, char c)
    {
        ArgumentNullException.ThrowIfNull(text);
        int best = 0, current = 0;
        foreach (var ch in text)
        {
            current = ch == c ? current + 1 : 0;
            best = Math.Max(best, current);
        }

        return best;
    }
}

/// <summary>
/// Owner keywords without a morphological analyser: Hangul/Latin words of 2+ characters, common
/// particles stripped, stop words and pseudonym tokens (<c>[인물1]</c>) removed.
/// </summary>
public static class KeywordExtractor
{
    private static readonly string[] Particles = ["에서", "으로", "한테", "에게", "이랑", "은", "는", "이", "가", "을", "를", "에", "도", "로", "랑", "와", "과", "의"];

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "그리고", "근데", "그래서", "진짜", "그냥", "나도", "내가", "너도", "니가", "이거", "그거", "저거", "오늘", "지금", "우리",
        "그럼", "그래", "아니", "그게", "이제", "많이", "너무", "정말", "완전", "약간", "하고", "해서", "했어", "있어", "없어",
        "같아", "같은", "이건", "그건",
    };

    public static IReadOnlyList<(string Word, int Count)> Top(IEnumerable<string> texts, int count)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var text in texts)
        {
            foreach (var raw in WithoutTokens(text).Split((char[])[' ', '\n', '\t', ',', '.', '!', '?', '~', '…', '"', '\'', '(', ')'], StringSplitOptions.RemoveEmptyEntries))
            {
                var word = Normalize(raw);
                if (word.Length >= 2 && !StopWords.Contains(word) && !word.All(c => c is 'ㅋ' or 'ㅎ' or 'ㅠ' or 'ㅜ'))
                {
                    counts[word] = counts.GetValueOrDefault(word) + 1;
                }
            }
        }

        return [.. counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(count).Select(kv => (kv.Key, kv.Value))];
    }

    /// <summary>Removes <c>[…]</c> tokens and anything attached to them ("[인물1]이랑" → " ").</summary>
    private static string WithoutTokens(string text)
    {
        var builder = new StringBuilder(text.Length);
        var depth = 0;
        var attached = false;
        foreach (var c in text)
        {
            if (c == '[')
            {
                depth++;
            }
            else if (c == ']' && depth > 0)
            {
                depth--;
                attached = depth == 0;
                builder.Append(' ');
            }
            else if (depth == 0 && !(attached && !char.IsWhiteSpace(c)))
            {
                attached = false;
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static string Normalize(string word)
    {
        var letters = new string([.. word.Where(c => c is >= '가' and <= '힣' || char.IsAsciiLetter(c))]);
        if (letters.Length == 0)
        {
            return string.Empty;
        }

        foreach (var particle in Particles)
        {
            if (letters.Length > particle.Length + 1 && letters.EndsWith(particle, StringComparison.Ordinal))
            {
                return letters[..^particle.Length];
            }
        }

        return char.IsAsciiLetter(letters[0]) ? letters.ToUpperInvariant() : letters;
    }
}
