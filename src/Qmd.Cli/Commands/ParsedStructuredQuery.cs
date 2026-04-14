using Qmd.Core.Models;

namespace Qmd.Cli.Commands;

internal record ParsedStructuredQuery(List<ExpandedQuery> Queries, string? Intent);
