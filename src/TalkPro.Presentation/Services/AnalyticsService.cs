using System.Security.Cryptography;
using System.Text.Json;
using TalkPro.Core.Model;
using TalkPro.Infrastructure.Vault;
using TalkPro.Persona.Analysis;
using TalkPro.Privacy;
using TalkPro.Security.Keys;

namespace TalkPro.Presentation.Services;

public enum AnalyticsError
{
    None,

    /// <summary>The dataset key or snapshot cannot be opened (deleted, damaged or another Windows user).</summary>
    Unreadable,

    /// <summary>Imported before the privacy review existed; analysis needs reviewed data (DD-020).</summary>
    NotPrivacyReviewed,

    NoOwner,
}

public sealed record AnalyticsResult(AnalyticsError Error, CommunicationReport? Report)
{
    public override string ToString() => $"AnalyticsResult {{ Error = {Error}, Report = {Report} }}";
}

public interface IAnalyticsService
{
    Task<AnalyticsResult> AnalyzeAsync(DatasetId id, CancellationToken cancellationToken);
}

/// <summary>
/// Opens a dataset, renders the Analysis view (excluded messages dropped, "Mask" decisions applied),
/// splits sessions and computes the owner-centred report on a worker thread. Nothing is persisted;
/// the session (key and in-memory database) is disposed before returning.
/// </summary>
public sealed class AnalyticsService(DatasetRepository repository, ISessionizer sessionizer) : IAnalyticsService
{
    public Task<AnalyticsResult> AnalyzeAsync(DatasetId id, CancellationToken cancellationToken) =>
        Task.Run(() => Analyze(id), cancellationToken);

    private AnalyticsResult Analyze(DatasetId id)
    {
        try
        {
            using var session = repository.Open(id);
            if (!session.PrivacyReviewed)
            {
                return new AnalyticsResult(AnalyticsError.NotPrivacyReviewed, null);
            }

            if (session.OwnerId is not { } owner)
            {
                return new AnalyticsResult(AnalyticsError.NoOwner, null);
            }

            var view = PurposeRedactor.RenderAll(session.LoadMessages(), session.LoadFindings(), session.LoadExcludedMessageIds(), RedactionPurpose.Analysis);
            var sessions = sessionizer.Split(view);
            return new AnalyticsResult(AnalyticsError.None, CommunicationAnalyzer.Analyze(owner, session.LoadParticipants(), view, sessions));
        }
        catch (Exception ex) when (ex is DatasetKeyNotFoundException or CryptographicException or InvalidDataException or JsonException or IOException)
        {
            return new AnalyticsResult(AnalyticsError.Unreadable, null);
        }
    }
}
