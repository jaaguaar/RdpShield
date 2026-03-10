using System.Collections.Concurrent;
using System.Threading.Channels;
using RdpShield.Api;

namespace RdpShield.Service.Live;

public sealed class LiveEventHub
{
    private readonly ConcurrentDictionary<Guid, Channel<EventDto>> _subs = new();

    public (Guid id, ChannelReader<EventDto> reader) Subscribe()
    {
        var id = Guid.NewGuid();
        var ch = Channel.CreateUnbounded<EventDto>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        _subs[id] = ch;
        return (id, ch.Reader);
    }

    public void Unsubscribe(Guid id)
    {
        if (_subs.TryRemove(id, out var ch))
            ch.Writer.TryComplete();
    }

    public void Publish(EventDto evt)
    {
        foreach (var kv in _subs)
            kv.Value.Writer.TryWrite(evt);
    }
}