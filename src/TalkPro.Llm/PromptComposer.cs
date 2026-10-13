using System.Text;
using TalkPro.Core.Llm;

namespace TalkPro.Llm;

/// <summary>
/// Turns an <see cref="LlmRequest"/> into the user message, the same for every runtime (Ollama, on-device):
/// context lines inside delimiters, one line per item, then the task. Data cannot break out of its place:
/// delimiter look-alikes, line breaks and chat-template control tokens (<c>&lt;|…|&gt;</c>) inside data are neutralised.
/// </summary>
internal static class PromptComposer
{
    public const string ContextStart = "[대화 맥락 시작]";
    public const string ContextEnd = "[대화 맥락 끝]";

    public static string UserMessage(LlmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Context.Count == 0)
        {
            return request.Task;
        }

        var builder = new StringBuilder();
        builder.Append(ContextStart).Append('\n');
        foreach (var line in request.Context)
        {
            builder.Append(line.Label).Append(": ").Append(Neutralise(line.Text.Value)).Append('\n');
        }

        builder.Append(ContextEnd).Append("\n\n").Append(request.Task);
        return builder.ToString();
    }

    /// <summary>Data only: no fake context delimiter, no fake line/label, no chat-template control token.</summary>
    public static string Neutralise(string data) =>
        data.Replace("[대화 맥락", "(대화 맥락", StringComparison.Ordinal)
            .Replace("<|", "< |", StringComparison.Ordinal)
            .Replace("|>", "| >", StringComparison.Ordinal)
            .ReplaceLineEndings(" ");
}
