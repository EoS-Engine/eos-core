using EOS.Contracts;

namespace EOS.Learning;

/// <summary>WP-027: <see cref="StageEngine"/>'s per-transition outcome — never throws for a legitimate "not promoted" outcome (e.g. ROI Gate blocked, insufficient domain count); throwing is reserved for genuine caller/data errors.</summary>
public sealed record StagePromotionResult(bool Promoted, PipelineStage? ResultingStage, string Reason);
