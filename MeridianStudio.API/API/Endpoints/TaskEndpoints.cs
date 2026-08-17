using MeridianStudio.API.Application.Contracts;
using MeridianStudio.API.Application.Interfaces;
using MeridianStudio.API.Domain.Models;
using MeridianStudio.API.Infrastructure.Guard;

namespace MeridianStudio.API.API.Endpoints;

public static class TaskEndpoints
{
    public static IEndpointRouteBuilder MapTaskEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/execute-task", HandleAsync)
              .WithName("ExecuteTask")
              .WithSummary("Synthesise step-by-step execution logs and a compile-ready code template")
              .WithDescription(
                  "Accepts a task specification. Simulates a full build-and-test pipeline, " +
                  "returning timestamped OutputLogs and a C# code scaffold in GeneratedCodeTemplate. " +
                  "Identical requests return cached results to conserve tokens.")
              .WithTags("Task")
              .Produces<TaskSpec>(StatusCodes.Status200OK)
              .ProducesValidationProblem();

        return routes;
    }

    private static async Task<IResult> HandleAsync(
        ExecuteTaskRequest request,
        ITaskExecutionService service,
        CancellationToken ct)
    {
        var errors = InputGuard.ValidateTask(request);
        if (errors is not null) return Results.ValidationProblem(errors);

        var result = await service.ExecuteTaskAsync(request, ct);
        return Results.Ok(result);
    }
}
