using System.Globalization;
using System.Text;

namespace TalkPro.Tools.FormatProbe;

/// <summary>
/// Turns a line into a content-free structural skeleton:
/// digits → <c>⟦D4⟧</c>, Hangul / Latin / other words → <c>⟦H3⟧</c> / <c>⟦A5⟧</c> / <c>⟦X2⟧</c>
/// (length only), emoji and other symbols → <c>⟦E⟧</c>, control characters → <c>⟦C⟧</c>.
/// Only a fixed allow-list of format words (오전, 오후, 요일, "님이 들어왔습니다", AM/PM, …) and ASCII
/// punctuation are kept verbatim. After <see cref="MaxContentRuns"/> content tokens the rest is cut with "…",
/// so message bodies are never reproduced.
/// </summary>
public static class LineSkeleton
{
    public const int MaxContentRuns = 3;
    public const int MaxInputChars = 64;
    public const char Ellipsis = '…';

    /// <summary>Korean words that are part of export formats, not content.</summary>
    public static readonly IReadOnlySet<string> StructuralHangul = new HashSet<string>(StringComparer.Ordinal)
    {
        "오전", "오후", "년", "월", "일", "시", "분", "초",
        "월요일", "화요일", "수요일", "목요일", "금요일", "토요일", "일요일",
        "저장한", "날짜", "님과", "카카오톡", "대화", "대화방", "채팅방",
        "님이", "님을", "들어왔습니다", "나갔습니다", "초대했습니다", "내보냈습니다",
        "사진", "동영상", "이모티콘", "파일", "음성메시지", "삭제된", "메시지입니다", "메시지가", "삭제되었습니다",
        "보이스톡", "페이스톡", "송금", "장", "개",
    };

    /// <summary>Latin words that are part of export formats (English UI, synthetic formats).</summary>
    public static readonly IReadOnlySet<string> StructuralLatin = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "AM", "PM", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun",
        "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday",
        "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Sept", "Oct", "Nov", "Dec",
        "January", "February", "March", "April", "June", "July", "August", "September", "October", "November", "December",
        "KakaoTalk", "Chats", "Talk", "Date", "Saved", "Message", "joined", "left", "Photo", "Photos", "Video", "Emoticon", "File",
        "TalkPro", "Synthetic", "Export", "Room",
    };

    public static string Of(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var output = new StringBuilder();
        var contentRuns = 0;
        var i = 0;
        var limit = Math.Min(line.Length, MaxInputChars);

        while (i < limit)
        {
            var c = line[i];
            if (char.IsAsciiDigit(c))
            {
                var start = i;
                while (i < limit && char.IsAsciiDigit(line[i]))
                {
                    i++;
                }

                output.Append(Token('D', i - start));
                continue;
            }

            if (IsHangul(c) || char.IsAsciiLetter(c) || (char.IsLetter(c) && !char.IsSurrogate(c)))
            {
                var kind = IsHangul(c) ? 'H' : char.IsAsciiLetter(c) ? 'A' : 'X';
                var start = i;
                while (i < limit && SameKind(kind, line[i]))
                {
                    i++;
                }

                var word = line[start..i];
                var structural = kind switch
                {
                    'H' => StructuralHangul.Contains(word),
                    'A' => StructuralLatin.Contains(word),
                    _ => false,
                };

                if (structural)
                {
                    output.Append(word);
                }
                else
                {
                    output.Append(Token(kind, word.Length));
                    if (++contentRuns >= MaxContentRuns)
                    {
                        return output.Append(Ellipsis).ToString();
                    }
                }

                continue;
            }

            if (c is ' ' or '\t')
            {
                output.Append(c == '\t' ? "⇥" : " ");
                i++;
                continue;
            }

            if (char.IsControl(c))
            {
                output.Append("⟦C⟧");
                i++;
                continue;
            }

            if (c < 128)
            {
                output.Append(c);
                i++;
                continue;
            }

            // Emoji, symbols, supplementary characters: one token per rune.
            var rune = Rune.GetRuneAt(line, i);
            i += rune.Utf16SequenceLength;
            output.Append("⟦E⟧");
            if (++contentRuns >= MaxContentRuns)
            {
                return output.Append(Ellipsis).ToString();
            }
        }

        if (i < line.Length)
        {
            output.Append(Ellipsis);
        }

        return output.ToString();
    }

    private static string Token(char kind, int length) =>
        string.Create(CultureInfo.InvariantCulture, $"⟦{kind}{length}⟧");

    private static bool IsHangul(char c) => c is (>= '가' and <= '힣') or (>= 'ㄱ' and <= 'ㆎ');

    private static bool SameKind(char kind, char c) => kind switch
    {
        'H' => IsHangul(c),
        'A' => char.IsAsciiLetter(c),
        _ => char.IsLetter(c) && !IsHangul(c) && !char.IsAsciiLetter(c) && !char.IsSurrogate(c),
    };
}
