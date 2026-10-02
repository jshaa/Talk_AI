using TalkPro.Tools.SampleGenerator.Corpus;

namespace TalkPro.Tools.SampleGenerator.Formats;

/// <summary>W1 "Windows-like": date blocks, 24h <c>[name] [HH:mm] text</c>.</summary>
public sealed class W1Format : SyntheticFormat
{
    private static readonly string[] DayNames = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    public override string Prefix => "w1";

    public override string FormatId => "synthetic.windows.v1";

    protected override char Family => 'W';

    protected override string SavedLine => "Saved: 2026-10-02 14:23:11";

    public override bool IsReserved(string line) =>
        line.StartsWith('[') || line.StartsWith("* [", StringComparison.Ordinal) || line.StartsWith("=== ", StringComparison.Ordinal);

    protected override DateTime Truncate(DateTime time) => time.AddTicks(-(time.Ticks % TimeSpan.TicksPerMinute));

    protected override bool IsRepresentableSpeaker(string speaker) => !speaker.Contains(']', StringComparison.Ordinal);

    protected override void WriteMessage(SampleBuilder builder, MessageItem message)
    {
        EnsureDate(builder, DateOnly.FromDateTime(message.Time));
        var record = "[" + message.Speaker + "] [" + Invariant(message.Time, "HH:mm") + "] " + message.TextLines[0];
        builder.StartMessage(record, message.Time, message.Speaker, message.TextLines);
    }

    protected override void WriteSystem(SampleBuilder builder, SystemItem system)
    {
        EnsureDate(builder, DateOnly.FromDateTime(system.Time));
        builder.System("* [" + Invariant(system.Time, "HH:mm") + "] " + system.Text, system.Time, system.Text, system.ExpectedEvent);
    }

    protected override void WriteMalformed(SampleBuilder builder, MalformationKind kind)
    {
        const string Text = "합성 테스트 메시지 (malformed)";
        switch (kind)
        {
            // Invalid date markers also clear the date context (format rule).
            case MalformationKind.InvalidDate:
                InvalidMarker(builder, "=== 2026-02-30 (Mon) ===", ExpectedIssue.InvalidTimestamp);
                break;
            case MalformationKind.ZeroYear:
                InvalidMarker(builder, "=== 0000-01-01 (Sat) ===", ExpectedIssue.InvalidTimestamp);
                break;
            case MalformationKind.AbsurdNumbers:
                InvalidMarker(builder, "=== 9999-99-99 (???) ===", ExpectedIssue.InvalidTimestamp);
                break;
            case MalformationKind.OutOfRangeDate:
                InvalidMarker(builder, "=== 2101-01-01 (Sat) ===", ExpectedIssue.TimestampOutOfRange);
                break;
            case MalformationKind.InvalidTime:
                EnsureAnyDate(builder);
                builder.Malformed("[UserA] [25:61] " + Text, ExpectedIssue.InvalidTimestamp);
                break;
            case MalformationKind.TooLongSpeaker:
                EnsureAnyDate(builder);
                builder.Malformed("[" + SyntheticVocabulary.TooLongName + "] [15:15] " + Text, ExpectedIssue.SpeakerNameTooLong);
                break;
            case MalformationKind.BlankSpeaker:
                EnsureAnyDate(builder);
                builder.Malformed("[   ] [15:15] " + Text, ExpectedIssue.InvalidSpeakerName);
                break;
            case MalformationKind.TruncatedRecord:
                builder.Malformed("[UserA] [15:", ExpectedIssue.IncompleteRecord);
                break;
            case MalformationKind.MissingDateContext:
                if (builder.DateContext is not null)
                {
                    throw new InvalidOperationException("Synthetic corpus error: MissingDateContext requires no date context.");
                }

                builder.Malformed("[UserA] [15:15] " + Text, ExpectedIssue.MissingDateContext);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private static void InvalidMarker(SampleBuilder builder, string line, ExpectedIssue issue)
    {
        builder.Malformed(line, issue);
        builder.DateContext = null;
    }

    private static void EnsureAnyDate(SampleBuilder builder)
    {
        if (builder.DateContext is null)
        {
            EnsureDate(builder, DefaultDate);
        }
    }

    private static void EnsureDate(SampleBuilder builder, DateOnly date)
    {
        if (builder.DateContext == date)
        {
            return;
        }

        var marker = "=== " + date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + " (" + DayNames[(int)date.DayOfWeek] + ") ===";
        builder.ContextRecord(marker);
        builder.DateContext = date;
    }
}
