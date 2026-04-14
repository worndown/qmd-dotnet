using Qmd.Core.Models;

namespace Qmd.Core.Indexing;

internal class ReindexOptions
{
    public List<string>? IgnorePatterns { get; init; }
    public IProgress<ReindexProgress>? Progress { get; init; }
}
