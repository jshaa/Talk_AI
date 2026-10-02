using TalkPro.Core.Diagnostics;

namespace TalkPro.Core.Parsing;

/// <summary>Constant identifier of an export format, e.g. <c>synthetic.windows.v1</c>. Log-safe.</summary>
public readonly record struct ChatFormatId : ILogSafe
{
    public ChatFormatId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        foreach (var c in value)
        {
            if (!(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '.' or '-'))
            {
                throw new ArgumentException("Format ids use lowercase ASCII letters, digits, '.' and '-' only.", nameof(value));
            }
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
