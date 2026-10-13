using TalkPro.Core.Model;
using TalkPro.Infrastructure.Vault;

namespace TalkPro.Presentation.Services;

public interface IDatasetCatalog
{
    IReadOnlyList<DatasetSummary> List();

    DatasetDeletionReport Delete(DatasetId id);

    /// <summary>Deletes every dataset in the vault (crypto-shredding each key first).</summary>
    IReadOnlyList<DatasetDeletionReport> DeleteAll();
}

public sealed class DatasetCatalog(DatasetRepository repository, DatasetDeletionService deletion) : IDatasetCatalog
{
    public IReadOnlyList<DatasetSummary> List() => repository.List();

    public DatasetDeletionReport Delete(DatasetId id) => deletion.Delete(id);

    public IReadOnlyList<DatasetDeletionReport> DeleteAll() => [.. repository.List().Select(s => deletion.Delete(s.Id))];
}
