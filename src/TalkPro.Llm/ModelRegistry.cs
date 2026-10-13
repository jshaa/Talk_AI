using System.Globalization;
using TalkPro.Core.Llm;

namespace TalkPro.Llm;

/// <summary>
/// Allow-list of local models (DD-023). A model that is not listed here is never used, even if installed.
/// <list type="bullet">
/// <item><see cref="ModelApproval.Approved"/>: commercial-use license confirmed and digest pinned. The only level Release accepts.</item>
/// <item><see cref="ModelApproval.Candidate"/>: license confirmed, digest not pinned. Debug only.</item>
/// <item><see cref="ModelApproval.LicenseReview"/>: custom terms with use restrictions. Debug only until reviewed.</item>
/// </list>
/// Licenses were checked on 2026-10-07; see docs/DEPENDENCIES.md.
/// </summary>
public sealed class ModelRegistry
{
    private const int DigestLength = 64;

    public ModelRegistry(IEnumerable<ModelSpec> models)
    {
        ArgumentNullException.ThrowIfNull(models);
        Models = [.. models];
        var duplicate = Models.GroupBy(m => Normalize(m.Id), StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException("Duplicate model id in the registry.", nameof(models));
        }

        foreach (var model in Models)
        {
            if (model.PinnedDigests.Any(d => d.Length != DigestLength || !d.All(char.IsAsciiHexDigitLower)))
            {
                throw new ArgumentException("Pinned digests must be lowercase sha256 hex.", nameof(models));
            }

            if (model.Approval == ModelApproval.Approved && model.PinnedDigests.Count == 0)
            {
                throw new ArgumentException("An approved model must have a pinned digest.", nameof(models));
            }
        }
    }

    /// <summary>Registry shipped with the app. Order = preference when several models are ready.</summary>
    public static ModelRegistry Default { get; } = new(
    [
        // Default chat model (owner decision 2026-10-07): Apache-2.0, Q4_K_M, digest pinned 2026-10-08.
        new ModelSpec("qwen3:8b", "Qwen3 8B", ModelRole.Chat, "Apache-2.0", new Uri("https://www.apache.org/licenses/LICENSE-2.0"), ModelApproval.Approved)
        {
            PinnedDigests = new HashSet<string>(StringComparer.Ordinal) { "500a1f067a9f782620b40bee6f7b0c89e17ae61f686b92c24933e4ca4b2b8b41" },
            IsReasoningModel = true,
        },
        new ModelSpec("gemma3:4b", "Gemma 3 4B", ModelRole.Chat, "Gemma Terms of Use", new Uri("https://ai.google.dev/gemma/terms"), ModelApproval.LicenseReview)
        {
            PinnedDigests = new HashSet<string>(StringComparer.Ordinal) { "a2af6cc3eb7fa8be8504abaf9b04e88f17a119ec3f04a3addf55f92841195f5a" },
        },
        // Default embedding model for memory search (owner approval 2026-10-13): MIT, 567M, 1024-dim, digest pinned 2026-10-13.
        new ModelSpec("bge-m3", "BGE-M3", ModelRole.Embedding, "MIT", new Uri("https://huggingface.co/BAAI/bge-m3"), ModelApproval.Approved)
        {
            PinnedDigests = new HashSet<string>(StringComparer.Ordinal) { "7907646426070047a77226ac3e684fbbe8410524f7b4a74d02837e43f2146bab" },
        },
    ]);

    public IReadOnlyList<ModelSpec> Models { get; }

    /// <summary>Ollama reports untagged models as <c>name:latest</c>.</summary>
    public static string Normalize(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var trimmed = id.Trim().ToLowerInvariant();
        return trimmed.Contains(':', StringComparison.Ordinal) ? trimmed : trimmed + ":latest";
    }

    /// <summary>Install state of every registry model given the runtime's installed (id → digest) list.</summary>
    public IReadOnlyList<ModelStatus> Evaluate(IReadOnlyDictionary<string, string> installed, ModelPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(policy);
        var byId = installed.ToDictionary(kv => Normalize(kv.Key), kv => kv.Value.ToLower(CultureInfo.InvariantCulture), StringComparer.Ordinal);
        return [.. Models.Select(spec =>
        {
            if (!byId.TryGetValue(Normalize(spec.Id), out var digest))
            {
                return new ModelStatus(spec, ModelInstallState.NotInstalled, null);
            }

            var state = spec.PinnedDigests.Count > 0 && !spec.PinnedDigests.Contains(digest) ? ModelInstallState.DigestMismatch
                : !policy.Allows(spec.Approval) ? ModelInstallState.NotApproved
                : ModelInstallState.Ready;
            return new ModelStatus(spec, state, digest);
        })];
    }
}

/// <summary>Which approval levels this build may use. Release: approved only; Debug: also candidates for development.</summary>
public sealed record ModelPolicy(bool AllowUnapproved)
{
    public static ModelPolicy ReleaseDefault { get; } = new(false);

    public bool Allows(ModelApproval approval) => approval == ModelApproval.Approved || AllowUnapproved;

    public override string ToString() => $"ModelPolicy {{ AllowUnapproved = {AllowUnapproved} }}";
}
