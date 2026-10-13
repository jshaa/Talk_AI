using TalkPro.Core.Llm;

namespace TalkPro.Llm.OnDevice;

/// <summary>One file of an on-device model with its pinned size and SHA-256 (lowercase hex).</summary>
public sealed record ModelFile(string Name, long Size, string Sha256)
{
    public override string ToString() => $"ModelFile {{ {Name}, {Size} bytes }}";
}

/// <summary>An ONNX Runtime GenAI model: where to fetch it (pinned commit), which files, and its chat template.</summary>
public sealed record OnDeviceModel(ModelSpec Spec, Uri BaseUri, IReadOnlyList<ModelFile> Files, string SystemFormat, string UserFormat, string AssistantStart)
{
    public long TotalBytes => Files.Sum(f => f.Size);

    public string Render(string system, string user) =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture, SystemFormat, system)
        + string.Format(System.Globalization.CultureInfo.InvariantCulture, UserFormat, user)
        + AssistantStart;

    public override string ToString() => $"OnDeviceModel {{ {Spec}, Files = {Files.Count} }}";
}

/// <summary>Allow-listed on-device models (DD-026). Hashes were verified on 2026-10-13 against the Hugging Face LFS ids.</summary>
public static class OnDeviceModels
{
    /// <summary>
    /// Microsoft Phi-4-mini-instruct, ONNX int4 for CPU and mobile (3.8B, MIT, Korean among its supported languages).
    /// About 4.9 GB; phones with 8 GB RAM or more recommended.
    /// </summary>
    public static OnDeviceModel Phi4MiniInt4 { get; } = new(
        new ModelSpec("phi-4-mini-instruct-onnx-int4", "Phi-4-mini (기기 안 실행)", ModelRole.Chat, "MIT", new Uri("https://huggingface.co/microsoft/Phi-4-mini-instruct-onnx"), ModelApproval.Approved)
        {
            PinnedDigests = new HashSet<string>(StringComparer.Ordinal) { "cb0267fa60befa1a4ade8c98b6d32a3d67f51abbd307c7f793f132e8d9092131" },
        },
        new Uri("https://huggingface.co/microsoft/Phi-4-mini-instruct-onnx/resolve/fc04c8f93df696602fd9f300a30d1bf2e3081347/cpu_and_mobile/cpu-int4-rtn-block-32-acc-level-4/"),
        [
            new("genai_config.json", 1520, "0fcfa1e663f2bc867f8dc62fae65dd0924f0a4d68b43d1234df742dd19171470"),
            new("config.json", 2504, "ac65d86061d3d0d704ee2511fd0eb8713ef19eb6eedba17c3080a4165d5b933b"),
            new("added_tokens.json", 249, "d4f2aceb0f20b71dd1f4bcc7e052e4412946bf281840b8f83d39f259571af486"),
            new("special_tokens_map.json", 587, "aff38493227d813e29fcf8406e8e90062f1f031aa47d589325e9c31d89ac7cc3"),
            new("tokenizer_config.json", 2960, "c565326a315fbe62cda093a59d298828c8f3f823122661325f41f3ba577a7dec"),
            new("tokenizer.json", 15524095, "382cc235b56c725945e149cc25f191da667c836655efd0857b004320e90e91ea"),
            new("vocab.json", 3910310, "6cb65a857824fa6615bb1782d95d882617a8bbce1da0317118586b36f39e98bd"),
            new("merges.txt", 2418348, "856ce61180bb689282eed6b3a6838bb1f438399be23aefe9d20eb379791fb4ad"),
            new("model.onnx", 52118230, "701aa5d185b6a782bc27104a990dd5b634fa507840b7c42f7ee6f1fb812d0b83"),
            new("model.onnx.data", 4856573952, "cb0267fa60befa1a4ade8c98b6d32a3d67f51abbd307c7f793f132e8d9092131"),
        ],
        "<|system|>{0}<|end|>",
        "<|user|>{0}<|end|>",
        "<|assistant|>");
}
