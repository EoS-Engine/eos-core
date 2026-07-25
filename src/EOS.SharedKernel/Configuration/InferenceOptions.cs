using System.ComponentModel.DataAnnotations;

namespace EOS.SharedKernel.Configuration;

public sealed record InferenceOptions
{
    [Required, MinLength(1)]
    public required string DefaultModel { get; init; }

    [Range(1, int.MaxValue)]
    public required int MaxTokens { get; init; }

    [Range(0.0, 2.0)]
    public required double Temperature { get; init; }
}
