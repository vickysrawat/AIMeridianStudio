using System.Text.Json;
using MeridianStudio.API.Application.Contracts;
using MeridianStudio.API.Application.Interfaces;
using MeridianStudio.API.Domain.Models;
using MeridianStudio.API.Infrastructure.Guard;

namespace MeridianStudio.API.API.Endpoints;

public static class AssessmentEndpoints
{
    public static IEndpointRouteBuilder MapAssessmentEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/assessment/stream", HandleStreamAsync)
              .WithName("StreamAssessment")
              .WithSummary("Stream a use-case Assessment via SSE")
              .WithDescription(
                  "Returns text/event-stream. Emits 'chunk' events with raw token text, " +
                  "then a final 'complete' event with the full Assessment JSON.")
              .WithTags("Assessment")
              .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
              .ProducesValidationProblem();

        routes.MapPatch("/assessment/{assessmentId}", HandlePatchAsync)
              .WithName("PatchAssessment")
              .WithSummary("Apply client/chat overrides to a cached assessment")
              .WithTags("Assessment")
              .Produces<Assessment>(StatusCodes.Status200OK)
              .Produces(StatusCodes.Status404NotFound);

        routes.MapPost("/assessment/{assessmentId}/chat", HandleChatAsync)
              .WithName("AssessmentChat")
              .WithSummary("Stream a section-scoped refinement conversation about a cached assessment")
              .WithTags("Assessment")
              .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
              .Produces(StatusCodes.Status404NotFound);

        routes.MapPost("/assessment/analyze", HandleAnalyzeAsync)
              .WithName("AnalyzeUseCase")
              .WithSummary("Critique a use-case brief and return a readiness review (advisory)")
              .WithDescription(
                  "Returns a readiness score, per-field status, clarifying questions, and " +
                  "one-click-applicable improvement suggestions. Does not run the assessment.")
              .WithTags("Assessment")
              .Produces<UseCaseReadiness>(StatusCodes.Status200OK)
              .ProducesValidationProblem();

        return routes;
    }

    private static async Task<IResult> HandleAnalyzeAsync(
        AssessmentRequest request,
        IUseCaseAnalysisService service,
        CancellationToken ct)
    {
        var errors = InputGuard.ValidateAssessment(request);
        if (errors is not null)
            return Results.ValidationProblem(errors);

        var result = await service.AnalyzeAsync(request, ct);
        return Results.Ok(result);
    }

    private static async Task HandleStreamAsync(
        AssessmentRequest request,
        IAssessmentService service,
        HttpContext ctx,
        CancellationToken ct)
    {
        var errors = InputGuard.ValidateAssessment(request);
        if (errors is not null)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        ctx.Response.Headers.ContentType  = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        ctx.Response.Headers.Connection   = "keep-alive";

        await foreach (var (evt, data) in service.StreamAssessmentAsync(request, ct))
        {
            var sseData = evt == "chunk" ? JsonSerializer.Serialize(data) : data;
            await ctx.Response.WriteAsync($"event: {evt}\ndata: {sseData}\n\n", ct);
            await ctx.Response.Body.FlushAsync(ct);
        }
    }

    private static async Task<IResult> HandlePatchAsync(
        string assessmentId,
        PatchAssessmentRequest patch,
        IAssessmentService service,
        CancellationToken ct)
    {
        var result = await service.PatchAssessmentAsync(assessmentId, patch, ct);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task HandleChatAsync(
        string assessmentId,
        BlueprintChatRequest request,
        IAssessmentService service,
        HttpContext ctx,
        CancellationToken ct)
    {
        ctx.Response.Headers.ContentType  = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        ctx.Response.Headers.Connection   = "keep-alive";

        await foreach (var (evt, data) in service.StreamChatAsync(assessmentId, request, ct))
        {
            var sseData = evt == "chunk" ? JsonSerializer.Serialize(data) : data;
            await ctx.Response.WriteAsync($"event: {evt}\ndata: {sseData}\n\n", ct);
            await ctx.Response.Body.FlushAsync(ct);
        }
    }
}
