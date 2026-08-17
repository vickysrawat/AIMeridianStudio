using MeridianStudio.API.Application.Contracts;
using MeridianStudio.API.Application.Interfaces;
using MeridianStudio.API.Domain.Models;
using MeridianStudio.API.Infrastructure.ExampleBank;
using MeridianStudio.API.Infrastructure.Guard;

namespace MeridianStudio.API.API.Endpoints;

public static class MissionEndpoints
{
    public static IEndpointRouteBuilder MapMissionEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/mission-suggestions", HandleSuggestionsAsync)
              .WithName("GetMissionSuggestions")
              .WithSummary("Generate contextual tone, goal, and criteria suggestions for a document type")
              .WithDescription(
                  "Returns LLM-generated tone options, goal options, and criteria sets grounded in " +
                  "the provided domain, solution type, and blueprint context. " +
                  "Past user selections in similar contexts are injected as few-shot examples.")
              .WithTags("Mission")
              .Produces<MissionSuggestions>(StatusCodes.Status200OK);

        routes.MapPost("/mission-suggestions/record", HandleRecordAsync)
              .WithName("RecordMissionSelection")
              .WithSummary("Record a user's mission selection as a training signal")
              .WithDescription(
                  "Called immediately when the user clicks 'Generate Document'. " +
                  "Records the selected tone, goal, and criteria regardless of document outcome. " +
                  "These selections inform future suggestion rankings for the same context.")
              .WithTags("Mission")
              .Produces(StatusCodes.Status204NoContent);

        return routes;
    }

    private static async Task<IResult> HandleSuggestionsAsync(
        MissionSuggestionsRequest request,
        IMissionSuggestionService service,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.TemplateType))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.TemplateType)] = ["TemplateType is required."]
            });

        var result = await service.GetSuggestionsAsync(request, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> HandleRecordAsync(
        RecordSelectionRequest request,
        SelectionBankService selectionBank,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.TemplateType) ||
            string.IsNullOrWhiteSpace(request.SelectedTone) ||
            string.IsNullOrWhiteSpace(request.SelectedGoal))
            return Results.NoContent(); // silent no-op on bad input

        var safeGoal = InputGuard.Sanitize(request.SelectedGoal, 500) ?? string.Empty;

        await selectionBank.RecordAsync(
            request.TemplateType,
            request.Domain ?? string.Empty,
            request.SolutionType ?? string.Empty,
            request.SelectedTone,
            safeGoal,
            request.SelectedCriteria ?? [],
            request.WasRefined,
            ct);

        return Results.NoContent();
    }
}
