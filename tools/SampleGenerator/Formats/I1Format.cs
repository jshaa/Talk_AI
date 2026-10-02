using TalkPro.Tools.SampleGenerator.Corpus;

namespace TalkPro.Tools.SampleGenerator.Formats;

/// <summary>I1 "iOS-like": dotted date with seconds, tab separated fields, speaker <c>*</c> = system.</summary>
public sealed class I1Format : SyntheticFormat
{
    public override string Prefix => "i1";

    public override string FormatId => "synthetic.ios.v1";

    protected override char Family => 'I';

    protected override string SavedLine => "Saved: 2026.10.02 14:23:11";

    public override bool IsReserved(string line) =>
        line.Length >= 11
        && char.IsAsciiDigit(line[0]) && char.IsAsciiDigit(line[1]) && char.IsAsciiDigit(line[2]) && char.IsAsciiDigit(line[3])
        && line[4] == '.' && char.IsAsciiDigit(line[5]) && char.IsAsciiDigit(line[6])
        && line[7] == '.' && char.IsAsciiDigit(line[8]) && char.IsAsciiDigit(line[9])
        && line[10] == ' ';

    protected override DateTime Truncate(DateTime time) => time.AddTicks(-(time.Ticks % TimeSpan.TicksPerSecond));

    protected override bool IsRepresentableSpeaker(string speaker) => !speaker.Contains('\t', StringComparison.Ordinal);

    protected override void WriteMessage(SampleBuilder builder, MessageItem message)
    {
        var record = Stamp(message.Time) + "\t" + message.Speaker + "\t" + message.TextLines[0];
        builder.StartMessage(record, message.Time, message.Speaker, message.TextLines);
    }

    protected override void WriteSystem(SampleBuilder builder, SystemItem system) =>
        builder.System(Stamp(system.Time) + "\t*\t" + system.Text, system.Time, system.Text, system.ExpectedEvent);

    protected override void WriteMalformed(SampleBuilder builder, MalformationKind kind)
    {
        const string Tail = "\tUserA\t합성 테스트 메시지 (malformed)";
        var (line, issue) = kind switch
        {
            MalformationKind.InvalidDate => ("2026.02.30 15:15:00" + Tail, ExpectedIssue.InvalidTimestamp),
            MalformationKind.InvalidTime => ("2026.10.01 25:61:00" + Tail, ExpectedIssue.InvalidTimestamp),
            MalformationKind.ZeroYear => ("0000.01.01 09:00:00" + Tail, ExpectedIssue.InvalidTimestamp),
            MalformationKind.AbsurdNumbers => ("9999.99.99 99:99:99" + Tail, ExpectedIssue.InvalidTimestamp),
            MalformationKind.OutOfRangeDate => ("2101.01.01 09:00:00" + Tail, ExpectedIssue.TimestampOutOfRange),
            MalformationKind.TooLongSpeaker => ("2026.10.01 15:15:00\t" + SyntheticVocabulary.TooLongName + "\t합성 테스트 메시지 (malformed)", ExpectedIssue.SpeakerNameTooLong),
            MalformationKind.BlankSpeaker => ("2026.10.01 15:15:00\t   \t합성 테스트 메시지 (malformed)", ExpectedIssue.InvalidSpeakerName),
            MalformationKind.TruncatedRecord => ("2026.10.01 15:15", ExpectedIssue.IncompleteRecord),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        builder.Malformed(line, issue);
    }

    private static string Stamp(DateTime time) => Invariant(time, "yyyy.MM.dd HH:mm:ss");
}
