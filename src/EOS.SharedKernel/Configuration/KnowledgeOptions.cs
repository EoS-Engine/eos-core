using System.ComponentModel.DataAnnotations;

namespace EOS.SharedKernel.Configuration;

public sealed record KnowledgeOptions
{
    [Required, MinLength(1)]
    public required string VectorStoreCollection { get; init; }
}
