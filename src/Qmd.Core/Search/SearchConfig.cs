namespace Qmd.Core.Search;

public class SearchConfig
{
    public double VecOnlyGateThreshold { get; init; } = 0.25;
    public double RerankGateThreshold { get; init; } = 0.05;
    public double ConfidenceGapRatio { get; init; } = 0.5;
    public double FtsMinSignal { get; init; } = 0.3;
}
