using Microsoft.UI.Dispatching;
using RdpShield.Api;
using RdpShield.Api.Client;

namespace RdpShield.Manager.Services;

public sealed class EventStreamService : IDisposable
{
    private readonly RdpShieldEventsStreamClient _client = new();

    private CancellationTokenSource? _cts;
    private DispatcherQueue? _ui;

    public event Action<EventDto>? EventReceived;

    public void Initialize(DispatcherQueue ui)
    {
        _ui ??= ui;
    }

    public void Start()
    {
        if (_cts != null) return;
        _cts = new CancellationTokenSource();

        _ = _client.RunAsync(evt =>
        {
            if (_ui is not null)
                _ui.TryEnqueue(() => EventReceived?.Invoke(evt));
            else
                EventReceived?.Invoke(evt);

            return Task.CompletedTask;
        }, _cts.Token);
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        _cts = null;
    }

    public void Dispose() => Stop();
}
