using TalkPro.Core.Diagnostics;

namespace TalkPro.Core.Llm;

public enum ModelRole
{
    Chat,
    Embedding,
}

public enum ModelApproval
{
    /// <summary>License confirmed for commercial use and the runtime digest is pinned. Usable in Release.</summary>
    Approved,

    /// <summary>License confirmed, digest not pinned yet. Debug builds only.</summary>
    Candidate,

    /// <summary>License carries extra use restrictions and needs review (e.g. custom model terms). Debug builds only.</summary>
    LicenseReview,
}

/// <summary>An allow-listed model. Only models in the registry are ever used.</summary>
public sealed record ModelSpec(
    string Id,
    string DisplayName,
    ModelRole Role,
    string License,
    Uri LicenseUrl,
    ModelApproval Approval) : ILogSafe
{
    /// <summary>Runtime digests (lowercase sha256 hex) accepted for this model. Empty = not pinned.</summary>
    public IReadOnlySet<string> PinnedDigests { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Model emits a reasoning phase by default (e.g. Qwen3); the runtime turns it off for drafts.</summary>
    public bool IsReasoningModel { get; init; }

    public override string ToString() => $"ModelSpec {{ Id = {Id}, Role = {Role}, Approval = {Approval} }}";
}

public enum ModelInstallState
{
    NotInstalled,

    /// <summary>Installed and allowed by the current policy.</summary>
    Ready,

    /// <summary>Installed, but its digest differs from every pinned digest.</summary>
    DigestMismatch,

    /// <summary>Installed, but the current build policy does not allow this approval level.</summary>
    NotApproved,
}

public sealed record ModelStatus(ModelSpec Spec, ModelInstallState State, string? InstalledDigest) : ILogSafe
{
    public override string ToString() => $"ModelStatus {{ Spec = {Spec}, State = {State} }}";
}

public enum RuntimeState
{
    Running,
    NotRunning,
    InvalidResponse,
}

public sealed record RuntimeStatus(RuntimeState State, string RuntimeName, string? Version, IReadOnlyList<ModelStatus> Models) : ILogSafe
{
    /// <summary>The model that will be used for <paramref name="role"/>: the first ready one in registry order.</summary>
    public ModelStatus? Selected(ModelRole role) => Models.FirstOrDefault(m => m.Spec.Role == role && m.State == ModelInstallState.Ready);

    public override string ToString() => $"RuntimeStatus {{ State = {State}, Runtime = {RuntimeName}, Models = {Models.Count} }}";
}

/// <summary>Health and model inventory of the local AI runtime (for the "로컬 AI" page).</summary>
public interface ILocalAiRuntime
{
    Task<RuntimeStatus> GetStatusAsync(CancellationToken cancellationToken);
}
