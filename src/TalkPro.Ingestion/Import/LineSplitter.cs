namespace TalkPro.Ingestion.Import;

public static class LineSplitter
{
    /// <summary>
    /// Splits decoded text into lines without terminators. LF and CRLF terminate a line; a lone CR is
    /// content. A terminator at the very end does not start another line, so empty text has no lines.
    /// </summary>
    public static IReadOnlyList<string> Split(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = new List<string>();
        var start = 0;
        while (start < text.Length)
        {
            var lineFeed = text.IndexOf('\n', start);
            if (lineFeed < 0)
            {
                lines.Add(text[start..]);
                break;
            }

            var end = lineFeed > start && text[lineFeed - 1] == '\r' ? lineFeed - 1 : lineFeed;
            lines.Add(text[start..end]);
            start = lineFeed + 1;
        }

        return lines;
    }
}
