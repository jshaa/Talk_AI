using System.Globalization;
using TalkPro.Tools.SampleGenerator.Corpus;

namespace TalkPro.Tools.SampleGenerator.Formats;

/// <summary>
/// Writer of one synthetic export format (<c>docs/SYNTHETIC_FORMATS.md</c>). Writers know only the
/// format specification, never the parser implementation, so golden expectations are independent.
/// </summary>
public abstract class SyntheticFormat
{
    public const string SignaturePrefix = "TalkPro Synthetic Export (";

    protected static readonly DateOnly DefaultDate = new(2026, 10, 1);

    public static IReadOnlyList<SyntheticFormat> All { get; } = [new W1Format(), new A1Format(), new I1Format()];

    /// <summary>File name prefix, e.g. <c>w1</c>.</summary>
    public abstract string Prefix { get; }

    /// <summary>Constant format id, e.g. <c>synthetic.windows.v1</c>.</summary>
    public abstract string FormatId { get; }

    /// <summary>Family letter used in the signature, e.g. <c>W</c>.</summary>
    protected abstract char Family { get; }

    protected abstract string SavedLine { get; }

    /// <summary>Whether a line starts with one of the format's reserved record prefixes.</summary>
    public abstract bool IsReserved(string line);

    public string Signature(int version) => SignaturePrefix + Family + version.ToString(CultureInfo.InvariantCulture) + ")";

    public RenderedSample Render(SampleCase sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        var builder = new SampleBuilder(this);

        if (sample.IncludeHeader)
        {
            if (sample.SignatureVersion == 1)
            {
                builder.Header(Signature(1), ExpectedOutcome.Metadata);
            }
            else
            {
                builder.Header(Signature(sample.SignatureVersion), ExpectedOutcome.Unsupported, ExpectedIssue.UnsupportedVersion);
            }

            builder.Header("Room: " + SyntheticVocabulary.RoomTitle, ExpectedOutcome.Metadata);
            builder.Header(SavedLine, ExpectedOutcome.Metadata);
            foreach (var extra in sample.ExtraHeaderLines)
            {
                builder.Header(extra, ExpectedOutcome.Metadata);
            }

            if (sample.Items.Count > 0)
            {
                builder.HeaderSeparator();
            }
        }

        foreach (var item in sample.Items)
        {
            switch (item)
            {
                case MessageItem message:
                    EnsureValidSpeaker(message.Speaker);
                    WriteMessage(builder, message with { Time = Truncate(message.Time) });
                    break;
                case SystemItem system:
                    WriteSystem(builder, system with { Time = Truncate(system.Time) });
                    break;
                case MalformedItem malformed:
                    WriteMalformed(builder, malformed.Kind);
                    break;
                case RawLineItem raw:
                    builder.Raw(raw.Line, raw.Expectation);
                    break;
                default:
                    throw new InvalidOperationException("Unknown sample item.");
            }
        }

        return builder.Build();
    }

    /// <summary>Drops the time components the format cannot represent.</summary>
    protected abstract DateTime Truncate(DateTime time);

    protected abstract void WriteMessage(SampleBuilder builder, MessageItem message);

    protected abstract void WriteSystem(SampleBuilder builder, SystemItem system);

    protected abstract void WriteMalformed(SampleBuilder builder, MalformationKind kind);

    /// <summary>Format-specific speaker restrictions (separator characters).</summary>
    protected abstract bool IsRepresentableSpeaker(string speaker);

    protected static string Invariant(DateTime time, string format) => time.ToString(format, CultureInfo.InvariantCulture);

    private void EnsureValidSpeaker(string speaker)
    {
        if (string.IsNullOrWhiteSpace(speaker) || speaker.Length > 128 || speaker == "*" || !IsRepresentableSpeaker(speaker))
        {
            throw new InvalidOperationException("Synthetic corpus error: speaker cannot be written as a valid record in " + Prefix + ".");
        }
    }
}
