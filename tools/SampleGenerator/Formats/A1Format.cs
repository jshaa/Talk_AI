using System.Globalization;
using TalkPro.Tools.SampleGenerator.Corpus;

namespace TalkPro.Tools.SampleGenerator.Formats;

/// <summary>A1 "Android-like": per-line date, Korean 12h clock, <c> | </c> separated fields.</summary>
public sealed class A1Format : SyntheticFormat
{
    public override string Prefix => "a1";

    public override string FormatId => "synthetic.android.v1";

    protected override char Family => 'A';

    protected override string SavedLine => "Saved: 2026-10-02 오후 2:23";

    public override bool IsReserved(string line) =>
        line.Length >= 11
        && char.IsAsciiDigit(line[0]) && char.IsAsciiDigit(line[1]) && char.IsAsciiDigit(line[2]) && char.IsAsciiDigit(line[3])
        && line[4] == '-' && char.IsAsciiDigit(line[5]) && char.IsAsciiDigit(line[6])
        && line[7] == '-' && char.IsAsciiDigit(line[8]) && char.IsAsciiDigit(line[9])
        && line[10] == ' ';

    protected override DateTime Truncate(DateTime time) => time.AddTicks(-(time.Ticks % TimeSpan.TicksPerMinute));

    protected override bool IsRepresentableSpeaker(string speaker) => !speaker.Contains(" |", StringComparison.Ordinal);

    protected override void WriteMessage(SampleBuilder builder, MessageItem message)
    {
        var record = Stamp(message.Time) + " | " + message.Speaker + " | " + message.TextLines[0];
        builder.StartMessage(record, message.Time, message.Speaker, message.TextLines);
    }

    protected override void WriteSystem(SampleBuilder builder, SystemItem system) =>
        builder.System(Stamp(system.Time) + " * " + system.Text, system.Time, system.Text, system.ExpectedEvent);

    protected override void WriteMalformed(SampleBuilder builder, MalformationKind kind)
    {
        const string Tail = " | UserA | 합성 테스트 메시지 (malformed)";
        var (line, issue) = kind switch
        {
            MalformationKind.InvalidDate => ("2026-02-30 오후 3:15" + Tail, ExpectedIssue.InvalidTimestamp),
            MalformationKind.InvalidTime => ("2026-10-01 오후 13:15" + Tail, ExpectedIssue.InvalidTimestamp),
            MalformationKind.ZeroYear => ("0000-01-01 오전 9:00" + Tail, ExpectedIssue.InvalidTimestamp),
            MalformationKind.AbsurdNumbers => ("9999-99-99 오후 99:99" + Tail, ExpectedIssue.InvalidTimestamp),
            MalformationKind.OutOfRangeDate => ("2101-01-01 오전 9:00" + Tail, ExpectedIssue.TimestampOutOfRange),
            MalformationKind.TooLongSpeaker => ("2026-10-01 오후 3:15 | " + SyntheticVocabulary.TooLongName + " | 합성 테스트 메시지 (malformed)", ExpectedIssue.SpeakerNameTooLong),
            MalformationKind.BlankSpeaker => ("2026-10-01 오후 3:15 |     | 합성 테스트 메시지 (malformed)", ExpectedIssue.InvalidSpeakerName),
            MalformationKind.TruncatedRecord => ("2026-10-01 오후 3:", ExpectedIssue.IncompleteRecord),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        builder.Malformed(line, issue);
    }

    /// <summary><c>yyyy-MM-dd 오전|오후 h:mm</c>; 00:xx is 오전 12:xx and 12:xx is 오후 12:xx.</summary>
    private static string Stamp(DateTime time)
    {
        var hour12 = time.Hour % 12 == 0 ? 12 : time.Hour % 12;
        var period = time.Hour < 12 ? "오전" : "오후";
        return Invariant(time, "yyyy-MM-dd") + " " + period + " " + hour12.ToString(CultureInfo.InvariantCulture) + ":" + Invariant(time, "mm");
    }
}
