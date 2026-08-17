using MeridianStudio.API.Application.Contracts;
using MeridianStudio.API.Domain.Models;

namespace MeridianStudio.API.Application.Interfaces;

/// <summary>
/// Critiques a research opportunity BEFORE a blueprint is generated and returns a readiness review
/// (score, per-field status, clarifying questions, one-click suggestions). Advisory only — the
/// opportunity→blueprint analog of <see cref="IUseCaseAnalysisService"/>.
/// </summary>
public interface IBlueprintReadinessService
{
    Task<UseCaseReadiness> AnalyzeAsync(GenerateBlueprintRequest request, CancellationToken ct = default);
}
