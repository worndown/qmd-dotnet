namespace Qmd.Core.Configuration;

internal interface IConfigSyncService
{
    void SyncToDb(CollectionConfig config);
}
