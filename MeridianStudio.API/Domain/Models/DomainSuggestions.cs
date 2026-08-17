namespace MeridianStudio.API.Domain.Models;

public sealed record DomainSuggestions
{
    public required List<DomainCategory> Domains { get; init; }
    public string ModelUsed { get; init; } = string.Empty;
}
