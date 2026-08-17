using MeridianStudio.API.Application.Contracts;
using MeridianStudio.API.Application.Services;
using MeridianStudio.API.Infrastructure.Guard;

namespace MeridianStudio.API.API.Endpoints;

public static class ProjectEndpoints
{
    public static IEndpointRouteBuilder MapProjectEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/generate-project", Handle)
              .WithName("GenerateProject")
              .WithSummary("Generate a complete, runnable project scaffold as a zip archive")
              .WithDescription(
                  "Packages the solution's integration-step code into a language-specific project structure " +
                  "(solution file, layer projects, domain model, DbContext, migration, tests, README) " +
                  "and returns it as application/zip. No LLM call is made — the archive is assembled " +
                  "from the StepCodes supplied in the request body plus hardcoded project templates.")
              .WithTags("Project")
              .Produces(StatusCodes.Status200OK, contentType: "application/zip")
              .ProducesValidationProblem();

        return routes;
    }

    private static IResult Handle(GenerateProjectRequest request)
    {
        var errors = InputGuard.ValidateProject(request);
        if (errors is not null) return Results.ValidationProblem(errors);

        var projectName = ProjectGeneratorService.GetProjectName(request.SolutionName);
        var zipBytes    = ProjectGeneratorService.GenerateZip(request);

        return Results.File(zipBytes, "application/zip", $"{projectName}.zip");
    }
}
