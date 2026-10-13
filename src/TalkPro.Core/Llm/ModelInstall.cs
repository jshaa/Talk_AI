using TalkPro.Core.Diagnostics;

namespace TalkPro.Core.Llm;

public enum ModelInstallError
{
    None,

    /// <summary>No connection, a server error, or a redirect to a host that is not allowed.</summary>
    Network,

    /// <summary>A downloaded file did not match its pinned SHA-256; it was deleted.</summary>
    HashMismatch,

    /// <summary>Not enough space or the model folder could not be written.</summary>
    Storage,

    Cancelled,
}

public sealed record ModelInstallProgress(long BytesDone, long BytesTotal) : ILogSafe
{
    public double Fraction => BytesTotal <= 0 ? 0 : (double)BytesDone / BytesTotal;

    public override string ToString() => $"ModelInstallProgress {{ {BytesDone}/{BytesTotal} }}";
}

/// <summary>
/// Downloads an on-device model (DD-014: model download is one of the three allowed network uses). Requests carry
/// only the file URL — never conversation data — and every file is verified against a pinned SHA-256.
/// </summary>
public interface IModelInstaller
{
    ModelSpec Model { get; }

    long TotalBytes { get; }

    bool IsInstalled { get; }

    Task<ModelInstallError> InstallAsync(IProgress<ModelInstallProgress>? progress, CancellationToken cancellationToken);

    /// <summary>Deletes the downloaded model files.</summary>
    void Remove();
}
