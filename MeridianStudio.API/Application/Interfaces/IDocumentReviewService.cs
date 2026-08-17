using MeridianStudio.API.Application.Contracts;
using MeridianStudio.API.Domain.Models;

namespace MeridianStudio.API.Application.Interfaces;

/// <summary>
/// Reviews a FINISHED document against domain / opportunity-fidelity / faithfulness axes the in-loop
/// goal judge never checks. Advisory only — never gates generation.
/// </summary>
public interface IDocumentReviewService
{
    Task<DocumentReview> ReviewAsync(DocumentReviewRequest request, CancellationToken ct = default);
}
