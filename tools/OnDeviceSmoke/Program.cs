using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using TalkPro.Core.Llm;
using TalkPro.Llm.OnDevice;

// A fixed synthetic prompt only: this tool never reads conversation data.
Console.OutputEncoding = System.Text.Encoding.UTF8;
var model = OnDeviceModels.Phi4MiniInt4;
var directory = args.Length > 0
    ? args[0]
    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TalkProDev", "models", model.Spec.Id);
if (!ModelDownloader.IsVerified(model, directory))
{
    Console.WriteLine("Model folder is missing files or the verification marker.");
    return 2;
}

using var client = new OnnxGenAiClient(model, directory, NullLogger<OnnxGenAiClient>.Instance);
var result = await client.GenerateAsync(
    new LlmRequest("한 문장으로만 답하세요.", [], "'오늘 날씨 좋다'는 메시지에 어울리는 짧은 한국어 답장을 JSON으로 쓰세요.")
    {
        JsonSchema = StructuredOutput.SchemaFor<Reply>(),
        Temperature = 0,
        MaxOutputTokens = 96,
    },
    CancellationToken.None);

var parsed = StructuredOutput.TryParse<Reply>(result.Text, out var reply);
Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{result} | {result.Elapsed.TotalSeconds:F1}s | schema ok = {parsed}"));
Console.WriteLine(reply?.Text);
return result.Succeeded && parsed ? 0 : 1;

internal sealed record Reply(string Text);
