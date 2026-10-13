using System.Globalization;
using TalkPro.Tools.SampleGenerator.Corpus;

namespace TalkPro.Tools.SampleGenerator.Writers;

/// <summary>A deliberately broken line and the issue code a parser must report for it.</summary>
public readonly record struct MalformedLine(string Line, string Issue, bool IsDateMarker);

/// <summary>
/// Renders logical items in one synthetic format (docs/SYNTHETIC_FORMATS.md).
/// Written independently from the TalkPro parsers on purpose.
/// </summary>
public abstract class FormatWriter
{
    protected static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public abstract string FormatId { get; }

    /// <summary>Golden sub-folder, e.g. <c>w1</c>.</summary>
    public abstract string Folder { get; }

    public abstract IReadOnlyList<string> HeaderLines { get; }

    public virtual bool UsesDateMarkers => false;

    public virtual string DateMarker(DateOnly day) => throw new NotSupportedException();

    public abstract string Message(DateTime time, string speaker, string firstLine);

    public abstract string System(DateTime time, string text);

    public abstract MalformedLine Malformed(MalformationKind kind);

    /// <summary>True if a line starting like this would be read as a record (not allowed as continuation).</summary>
    public abstract bool IsReservedPrefix(string line);

    /// <summary>Characters/sequences a speaker name must not contain in this format.</summary>
    public abstract bool IsValidSpeaker(string speaker);

    protected static string TooLongName => SyntheticVocabulary.TooLongName;
}

public sealed class WindowsW1Writer : FormatWriter
{
    public override string FormatId => "synthetic.windows.v1";

    public override string Folder => "w1";

    public override IReadOnlyList<string> HeaderLines { get; } =
        ["TalkPro Synthetic Export (W1)", "Room: " + SyntheticVocabulary.RoomTitle, "Saved: 2026-10-02 14:23:11", ""];

    public override bool UsesDateMarkers => true;

    public override string DateMarker(DateOnly day) =>
        $"=== {day.ToString("yyyy-MM-dd", Invariant)} ({day.ToString("ddd", Invariant)}) ===";

    public override string Message(DateTime time, string speaker, string firstLine) =>
        $"[{speaker}] [{time.ToString("HH:mm", Invariant)}] {firstLine}";

    public override string System(DateTime time, string text) => $"* [{time.ToString("HH:mm", Invariant)}] {text}";

    public override MalformedLine Malformed(MalformationKind kind) => kind switch
    {
        MalformationKind.InvalidDate => new("=== 2026-13-45 (Thu) ===", "InvalidTimestamp", true),
        MalformationKind.InvalidTime => new("[UserA] [25:99] 시간 오류 합성 메시지", "InvalidTimestamp", false),
        MalformationKind.ZeroYear => new("=== 0000-01-01 (Mon) ===", "InvalidTimestamp", true),
        MalformationKind.OutOfRangeDate => new("=== 2999-01-01 (Tue) ===", "TimestampOutOfRange", true),
        MalformationKind.TooLongSpeaker => new($"[{TooLongName}] [10:02] 긴 이름 합성 메시지", "SpeakerNameTooLong", false),
        MalformationKind.BlankSpeaker => new("[   ] [10:02] 이름 없는 합성 메시지", "InvalidSpeakerName", false),
        MalformationKind.TruncatedRecord => new("[UserA] [10:0", "IncompleteRecord", false),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public override bool IsReservedPrefix(string line) =>
        line.StartsWith('[') || line.StartsWith("* [", StringComparison.Ordinal) || line.StartsWith("=== ", StringComparison.Ordinal);

    public override bool IsValidSpeaker(string speaker) => !speaker.Contains(']', StringComparison.Ordinal);
}

public sealed class AndroidA1Writer : FormatWriter
{
    public override string FormatId => "synthetic.android.v1";

    public override string Folder => "a1";

    public override IReadOnlyList<string> HeaderLines { get; } =
        ["TalkPro Synthetic Export (A1)", "Room: " + SyntheticVocabulary.RoomTitle, "Saved: 2026-10-02 오후 2:23", ""];

    public override string Message(DateTime time, string speaker, string firstLine) => $"{Stamp(time)} | {speaker} | {firstLine}";

    public override string System(DateTime time, string text) => $"{Stamp(time)} * {text}";

    public override MalformedLine Malformed(MalformationKind kind) => kind switch
    {
        MalformationKind.InvalidDate => new("2026-13-45 오후 3:00 | UserA | 날짜 오류 합성 메시지", "InvalidTimestamp", false),
        MalformationKind.InvalidTime => new("2026-03-01 오후 13:00 | UserA | 시간 오류 합성 메시지", "InvalidTimestamp", false),
        MalformationKind.ZeroYear => new("0000-01-01 오전 9:00 | UserA | 연도 오류 합성 메시지", "InvalidTimestamp", false),
        MalformationKind.OutOfRangeDate => new("2999-01-01 오전 9:00 | UserA | 범위 밖 합성 메시지", "TimestampOutOfRange", false),
        MalformationKind.TooLongSpeaker => new($"2026-03-01 오전 10:02 | {TooLongName} | 긴 이름 합성 메시지", "SpeakerNameTooLong", false),
        MalformationKind.BlankSpeaker => new("2026-03-01 오전 10:02 |     | 이름 없는 합성 메시지", "InvalidSpeakerName", false),
        MalformationKind.TruncatedRecord => new("2026-03-01 오전 10:", "IncompleteRecord", false),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public override bool IsReservedPrefix(string line) =>
        line.Length >= 11 && IsDigits(line, 0, 4) && line[4] == '-' && IsDigits(line, 5, 2) && line[7] == '-' && IsDigits(line, 8, 2) && line[10] == ' ';

    public override bool IsValidSpeaker(string speaker) => !speaker.Contains(" | ", StringComparison.Ordinal) && speaker != "*";

    private static string Stamp(DateTime time)
    {
        var meridiem = time.Hour < 12 ? "오전" : "오후";
        var hour12 = time.Hour % 12 == 0 ? 12 : time.Hour % 12;
        return $"{time.ToString("yyyy-MM-dd", Invariant)} {meridiem} {hour12.ToString(Invariant)}:{time.ToString("mm", Invariant)}";
    }

    private static bool IsDigits(string s, int start, int count)
    {
        for (var i = start; i < start + count; i++)
        {
            if (!char.IsAsciiDigit(s[i]))
            {
                return false;
            }
        }

        return true;
    }
}

public sealed class IosI1Writer : FormatWriter
{
    public override string FormatId => "synthetic.ios.v1";

    public override string Folder => "i1";

    public override IReadOnlyList<string> HeaderLines { get; } =
        ["TalkPro Synthetic Export (I1)", "Room: " + SyntheticVocabulary.RoomTitle, "Saved: 2026.10.02 14:23:11", ""];

    public override string Message(DateTime time, string speaker, string firstLine) =>
        $"{time.ToString("yyyy.MM.dd HH:mm:ss", Invariant)}\t{speaker}\t{firstLine}";

    public override string System(DateTime time, string text) => $"{time.ToString("yyyy.MM.dd HH:mm:ss", Invariant)}\t*\t{text}";

    public override MalformedLine Malformed(MalformationKind kind) => kind switch
    {
        MalformationKind.InvalidDate => new("2026.13.45 15:00:00\tUserA\t날짜 오류 합성 메시지", "InvalidTimestamp", false),
        MalformationKind.InvalidTime => new("2026.03.01 25:61:00\tUserA\t시간 오류 합성 메시지", "InvalidTimestamp", false),
        MalformationKind.ZeroYear => new("0000.01.01 09:00:00\tUserA\t연도 오류 합성 메시지", "InvalidTimestamp", false),
        MalformationKind.OutOfRangeDate => new("2999.01.01 09:00:00\tUserA\t범위 밖 합성 메시지", "TimestampOutOfRange", false),
        MalformationKind.TooLongSpeaker => new($"2026.03.01 10:02:00\t{TooLongName}\t긴 이름 합성 메시지", "SpeakerNameTooLong", false),
        MalformationKind.BlankSpeaker => new("2026.03.01 10:02:00\t   \t이름 없는 합성 메시지", "InvalidSpeakerName", false),
        MalformationKind.TruncatedRecord => new("2026.03.01 10:0", "IncompleteRecord", false),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public override bool IsReservedPrefix(string line) =>
        line.Length >= 11 && char.IsAsciiDigit(line[0]) && char.IsAsciiDigit(line[1]) && char.IsAsciiDigit(line[2]) && char.IsAsciiDigit(line[3])
        && line[4] == '.' && char.IsAsciiDigit(line[5]) && char.IsAsciiDigit(line[6]) && line[7] == '.'
        && char.IsAsciiDigit(line[8]) && char.IsAsciiDigit(line[9]) && line[10] == ' ';

    public override bool IsValidSpeaker(string speaker) => !speaker.Contains('\t', StringComparison.Ordinal) && speaker != "*";
}
