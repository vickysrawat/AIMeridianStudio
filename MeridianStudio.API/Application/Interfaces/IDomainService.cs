using MeridianStudio.API.Domain.Models;

namespace MeridianStudio.API.Application.Interfaces;

public interface IDomainService
{
    Task<DomainSuggestions> DiscoverDomainsAsync(CancellationToken ct = default);
}
