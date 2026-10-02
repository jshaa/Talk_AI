using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TalkPro.Core.Diagnostics;

namespace TalkPro.Infrastructure.Logging;

/// <summary>
/// Renders log entries without conversation content or personal data.
/// <list type="bullet">
/// <item>Only structured entries (static message template + named values) are rendered.</item>
/// <item>Values are rendered only if they are primitives, enums, times or <see cref="ILogSafe"/>; strings and other objects are redacted.</item>
/// <item>Exceptions are rendered as type, HResult and stack trace; <see cref="Exception.Message"/> and <see cref="Exception.Data"/> are never written.</item>
/// </list>
/// </summary>
public static partial class ContentFreeLogFormatter
{
    public const string UnstructuredEntry = "[unstructured entry redacted]";
    private const string OriginalFormatKey = "{OriginalFormat}";
    private const int MaxInnerExceptionDepth = 5;

    [GeneratedRegex(@"\{(?<name>[^{}:,]+)(?:[,:][^{}]*)?\}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Placeholder();

    public static string FormatState<TState>(TState state)
    {
        if (state is not IReadOnlyList<KeyValuePair<string, object?>> pairs)
        {
            return UnstructuredEntry;
        }

        string? template = null;
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in pairs)
        {
            if (string.Equals(pair.Key, OriginalFormatKey, StringComparison.Ordinal))
            {
                template = pair.Value as string;
            }
            else
            {
                values[pair.Key] = pair.Value;
            }
        }

        if (template is null)
        {
            return UnstructuredEntry;
        }

        return Placeholder().Replace(
            template,
            match => values.TryGetValue(match.Groups["name"].Value, out var value) ? RenderValue(value) : match.Value);
    }

    public static string RenderValue(object? value) => value switch
    {
        null => "null",
        ILogSafe safe => safe.ToString() ?? string.Empty,
        bool b => b ? "true" : "false",
        Enum e => e.ToString(),
        byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal
            or DateTime or DateTimeOffset or TimeSpan or Guid => ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture),
        _ => "[redacted:" + value.GetType().Name + "]",
    };

    public static string FormatException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var builder = new StringBuilder();
        AppendException(builder, exception, depth: 0);
        return builder.ToString();
    }

    private static void AppendException(StringBuilder builder, Exception exception, int depth)
    {
        builder.Append(exception.GetType().FullName)
               .Append(" (HResult=0x")
               .Append(exception.HResult.ToString("X8", CultureInfo.InvariantCulture))
               .Append(')');

        if (exception.StackTrace is { } stackTrace)
        {
            builder.AppendLine().Append(stackTrace);
        }

        if (depth >= MaxInnerExceptionDepth)
        {
            return;
        }

        IEnumerable<Exception> inner = exception is AggregateException aggregate
            ? aggregate.InnerExceptions
            : exception.InnerException is { } single ? [single] : [];

        foreach (var child in inner)
        {
            builder.AppendLine().Append(" ---> ");
            AppendException(builder, child, depth + 1);
        }
    }
}
