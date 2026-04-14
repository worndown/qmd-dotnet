using Qmd.Core.Models;

namespace Qmd.Core.Indexing;

internal interface IStatusRepository
{
    IndexStatus GetStatus();
    IndexHealthInfo GetIndexHealth();
}
