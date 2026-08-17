using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace MeridianStudio.API.Infrastructure.Realtime;

/// <summary>
/// Singleton fan-out broadcaster. The LLM orchestrator writes events here;
/// each connected SSE client holds a subscription channel and receives every event.
/// Register as Singleton.
/// </summary>
public sealed class ModelStatusBroadcaster
{
    private readonly List<ChannelWriter<ModelStatusEvent>> _writers = [];
    private readonly object _lock = new();

    /// <summary>Broadcast an event to all currently connected SSE clients.</summary>
    public void Broadcast(ModelStatusEvent evt)
    {
        lock (_lock)
        {
            foreach (var writer in _writers)
                writer.TryWrite(evt);
        }
    }

    /// <summary>
    /// Subscribe to the broadcast stream. Yields events until the client disconnects
    /// (i.e. <paramref name="ct"/> is cancelled).
    /// </summary>
    public async IAsyncEnumerable<ModelStatusEvent> SubscribeAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Bounded so a slow client doesn't accumulate unbounded memory.
        var channel = Channel.CreateBounded<ModelStatusEvent>(
            new BoundedChannelOptions(200)
            {
                FullMode          = BoundedChannelFullMode.DropOldest,
                SingleWriter      = false,
                SingleReader      = true
            });

        lock (_lock) _writers.Add(channel.Writer);

        try
        {
            await foreach (var evt in channel.Reader.ReadAllAsync(ct))
                yield return evt;
        }
        finally
        {
            lock (_lock)
            {
                _writers.Remove(channel.Writer);
                channel.Writer.TryComplete();
            }
        }
    }
}
