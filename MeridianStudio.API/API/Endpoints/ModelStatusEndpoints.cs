using System.Text.Json;
using System.Threading.Channels;
using MeridianStudio.API.Infrastructure.Realtime;

namespace MeridianStudio.API.API.Endpoints;

public static class ModelStatusEndpoints
{
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IEndpointRouteBuilder MapModelStatusEndpoints(
        this IEndpointRouteBuilder routes)
    {
        // SSE stream — client subscribes once and receives live model-routing events.
        // Standard Server-Sent Events (text/event-stream) format:
        //   data: {"type":"attempting","provider":"Gemini (gemini-2.5-flash)","operation":"research","timestamp":"..."}
        routes.MapGet("/events/model-status", HandleAsync)
              .WithName("ModelStatusStream")
              .WithTags("Events")
              .AllowAnonymous()   // EventSource can't attach bearer tokens; stream stays open
              .Produces(StatusCodes.Status200OK, contentType: "text/event-stream");

        return routes;
    }

    private static async Task HandleAsync(
        ModelStatusBroadcaster broadcaster,
        HttpResponse response,
        CancellationToken ct)
    {
        response.Headers.ContentType  = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection   = "keep-alive";
        // Prevent nginx / IIS from buffering SSE responses.
        response.Headers["X-Accel-Buffering"] = "no";

        // Initial handshake — lets the client know the stream is live.
        await WriteEventAsync(response, new ModelStatusEvent(
            "connected", "MeridianStudio API", "system", DateTimeOffset.UtcNow), ct);

        // Merge model-status events and periodic heartbeats through a single channel
        // so we never write to the response from two concurrent tasks.
        var merged = Channel.CreateBounded<ModelStatusEvent?>(
            new BoundedChannelOptions(200) { FullMode = BoundedChannelFullMode.DropOldest });

        // Producer 1 — model events from the broadcaster.
        var eventsTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var evt in broadcaster.SubscribeAsync(ct))
                    await merged.Writer.WriteAsync(evt, ct);
            }
            catch (OperationCanceledException) { }
            finally { merged.Writer.TryComplete(); }
        }, ct);

        // Producer 2 — heartbeat (null = send SSE comment, not a real event).
        var heartbeatTask = Task.Run(async () =>
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(25));
                while (await timer.WaitForNextTickAsync(ct))
                    await merged.Writer.WriteAsync(null, ct); // null → heartbeat marker
            }
            catch (OperationCanceledException) { }
        }, ct);

        // Single consumer — writes to response sequentially (no concurrency issues).
        try
        {
            await foreach (var item in merged.Reader.ReadAllAsync(ct))
            {
                if (item is null)
                {
                    // SSE comment line — keeps connection alive through proxies.
                    await response.WriteAsync(": heartbeat\n\n", ct);
                }
                else
                {
                    await WriteEventAsync(response, item, ct);
                }
                await response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — normal exit.
        }

        await Task.WhenAll(eventsTask, heartbeatTask);
    }

    private static async Task WriteEventAsync(
        HttpResponse response,
        ModelStatusEvent evt,
        CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(evt, _json);
        await response.WriteAsync($"data: {json}\n\n", ct);
    }
}
