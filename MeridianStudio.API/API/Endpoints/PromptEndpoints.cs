using MeridianStudio.API.Application.Contracts;
using MeridianStudio.API.Application.Interfaces;
using MeridianStudio.API.Domain.Models;
using MeridianStudio.API.Infrastructure.Guard;

namespace MeridianStudio.API.API.Endpoints;

public static class PromptEndpoints
{
    public static IEndpointRouteBuilder MapPromptEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/generate-component-prompt", HandleAsync)
              .WithName("GenerateComponentPrompt")
              .WithSummary("Compile a detailed developer handoff prompt targeting a specific LLM")
              .WithDescription(
                  "Accepts a componentName and optional targetLLM " +
                  "(Claude Sonnet | GPT-4o | Gemini 1.5 Pro — defaults to Claude Sonnet). " +
                  "Returns a DeveloperPrompt with PromptText and structured Directives " +
                  "ready to paste into any LLM IDE integration.")
              .WithTags("Prompt")
              .Produces<DeveloperPrompt>(StatusCodes.Status200OK)
              .ProducesValidationProblem();

        return routes;
    }

    private static async Task<IResult> HandleAsync(
        GenerateComponentPromptRequest request,
        IPromptService service,
        CancellationToken ct)
    {
        var errors = InputGuard.ValidatePrompt(request);
        if (errors is not null) return Results.ValidationProblem(errors);

        var result = await service.GeneratePromptAsync(request, ct);
        return Results.Ok(result);
    }
}
