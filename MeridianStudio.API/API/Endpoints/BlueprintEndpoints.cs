using System.Text.Json;
using MeridianStudio.API.Application.Contracts;
using MeridianStudio.API.Application.Interfaces;
using MeridianStudio.API.Domain.Models;
using MeridianStudio.API.Infrastructure.Guard;



namespace MeridianStudio.API.API.Endpoints;

public static class BlueprintEndpoints
{
    public static IEndpointRouteBuilder MapBlueprintEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/generate-blueprint", HandleAsync)
              .WithName("GenerateBlueprint")
              .WithSummary("Compile a full SystemBlueprint for a proposed solution")
              .WithDescription(
                  "Accepts a solutionId and solutionName. Optionally supply a domain " +
                  "to override automatic domain detection. Returns a detailed SystemBlueprint " +
                  "including topology, database schemas, endpoint manifest, and resilience strategies.")
              .WithTags("Blueprint")
              .Produces<SystemBlueprint>(StatusCodes.Status200OK)
              .ProducesValidationProblem();

        routes.MapPost("/generate-blueprint/stream", HandleStreamAsync)
              .WithName("StreamBlueprint")
              .WithSummary("Stream a SystemBlueprint via SSE as the LLM generates it")
              .WithDescription(
                  "Returns text/event-stream. Emits 'chunk' events with raw token text, " +
                  "then a final 'complete' event with the full SystemBlueprint JSON.")
              .WithTags("Blueprint")
              .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
              .ProducesValidationProblem();

        routes.MapPatch("/blueprint/{blueprintId}", HandlePatchAsync)
              .WithName("PatchBlueprint")
              .WithSummary("Apply client overrides to a cached blueprint")
              .WithDescription("Merges the supplied fields into the cached blueprint (e.g. edited arch decisions). Returns 404 if not found.")
              .WithTags("Blueprint")
              .Produces<SystemBlueprint>(StatusCodes.Status200OK)
              .Produces(StatusCodes.Status404NotFound);

        routes.MapPost("/blueprint/{blueprintId}/regenerate-topology", HandleRegenerateTopologyAsync)
              .WithName("RegenerateTopology")
              .WithSummary("Regenerate the system topology based on current blueprint state")
              .WithTags("Blueprint")
              .Produces(StatusCodes.Status200OK, contentType: "text/event-stream");

        routes.MapPost("/blueprint/{blueprintId}/chat", HandleChatAsync)
              .WithName("BlueprintChat")
              .WithSummary("Stream a section-scoped architect conversation about a cached blueprint")
              .WithDescription("Returns text/event-stream. Emits chunk events (display text), apply events (structured patch), and done.")
              .WithTags("Blueprint")
              .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
              .Produces(StatusCodes.Status404NotFound);

        routes.MapPost("/generate-blueprint/readiness", HandleReadinessAsync)
              .WithName("BlueprintReadiness")
              .WithSummary("Critique an opportunity for blueprint-readiness (advisory)")
              .WithDescription(
                  "Returns a readiness score, per-field status, clarifying questions, and one-click " +
                  "improvement suggestions for a research opportunity. Does not generate the blueprint.")
              .WithTags("Blueprint")
              .Produces<UseCaseReadiness>(StatusCodes.Status200OK)
              .ProducesValidationProblem();

        return routes;
    }

    private static async Task<IResult> HandleReadinessAsync(
        GenerateBlueprintRequest request,
        IBlueprintReadinessService service,
        CancellationToken ct)
    {
        var errors = InputGuard.ValidateBlueprint(request);
        if (errors is not null) return Results.ValidationProblem(errors);

        var result = await service.AnalyzeAsync(request, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> HandleAsync(
        GenerateBlueprintRequest request,
        IBlueprintService service,
        CancellationToken ct)
    {
        var errors = InputGuard.ValidateBlueprint(request);
        if (errors is not null) return Results.ValidationProblem(errors);

        var result = await service.GenerateBlueprintAsync(request, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> HandlePatchAsync(
        string blueprintId,
        PatchBlueprintRequest patch,
        IBlueprintService service,
        CancellationToken ct)
    {
        var result = await service.PatchBlueprintAsync(blueprintId, patch, ct);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task HandleRegenerateTopologyAsync(
        string blueprintId,
        IBlueprintService service,
        HttpContext ctx,
        CancellationToken ct)
    {
        ctx.Response.Headers.ContentType  = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        ctx.Response.Headers.Connection   = "keep-alive";

        await foreach (var (evt, data) in service.RegenerateTopologyAsync(blueprintId, ct))
        {
            var sseData = evt == "chunk" ? JsonSerializer.Serialize(data) : data;
            await ctx.Response.WriteAsync($"event: {evt}\ndata: {sseData}\n\n", ct);
            await ctx.Response.Body.FlushAsync(ct);
        }
    }

    private static async Task HandleChatAsync(
        string blueprintId,
        BlueprintChatRequest request,
        IBlueprintChatService chatService,
        HttpContext ctx,
        CancellationToken ct)
    {
        ctx.Response.Headers.ContentType  = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        ctx.Response.Headers.Connection   = "keep-alive";

        await foreach (var (evt, data) in chatService.StreamChatAsync(blueprintId, request, ct))
        {
            // chunk data may contain newlines — JSON-encode for safe SSE transport
            var sseData = evt == "chunk" ? JsonSerializer.Serialize(data) : data;
            await ctx.Response.WriteAsync($"event: {evt}\ndata: {sseData}\n\n", ct);
            await ctx.Response.Body.FlushAsync(ct);
        }
    }

    private static async Task HandleStreamAsync(
        GenerateBlueprintRequest request,
        IBlueprintService service,
        HttpContext ctx,
        CancellationToken ct)
    {
        var errors = InputGuard.ValidateBlueprint(request);
        if (errors is not null)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        ctx.Response.Headers.ContentType  = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        ctx.Response.Headers.Connection   = "keep-alive";

        await foreach (var (evt, data) in service.StreamBlueprintAsync(request, ct))
        {
            // Chunk data is raw LLM text and may contain newlines — JSON-encode it so
            // the SSE line format is not broken. Complete data is already a JSON object.
            var sseData = evt == "chunk" ? JsonSerializer.Serialize(data) : data;
            await ctx.Response.WriteAsync($"event: {evt}\ndata: {sseData}\n\n", ct);
            await ctx.Response.Body.FlushAsync(ct);
        }
    }
}
