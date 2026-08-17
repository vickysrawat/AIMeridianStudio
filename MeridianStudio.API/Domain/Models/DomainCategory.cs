namespace MeridianStudio.API.Domain.Models;

public sealed record DomainCategory
{
    public required string Name { get; init; }
    public required List<string> SubDomains { get; init; }
}
