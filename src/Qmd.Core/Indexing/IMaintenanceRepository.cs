namespace Qmd.Core.Indexing;

internal interface IMaintenanceRepository
{
    int DeleteInactiveDocuments();
    int DeleteLLMCache();
    int DeleteOrphanedCollectionDocuments();
    int CleanupOrphanedContent();
    int CleanupOrphanedVectors();
    void VacuumDatabase();
}
