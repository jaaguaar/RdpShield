using CommunityToolkit.Mvvm.ComponentModel;
using RdpShield.Api.Client;
using System.Threading;

namespace RdpShield.Manager.Services;

public sealed class ServiceConnectionState : ObservableObject, IDisposable
{
    private readonly IRdpShieldClient _client;
    private readonly PeriodicTimer _timer;
    private readonly SynchronizationContext? _uiContext;
    private CancellationTokenSource? _cts;

    private bool _isOnline;
    public bool IsOnline
    {
        get => _isOnline;
        private set => SetProperty(ref _isOnline, value);
    }

    private string? _lastError;
    public string? LastError
    {
        get => _lastError;
        private set => SetProperty(ref _lastError, value);
    }

    public ServiceConnectionState(IRdpShieldClient client, TimeSpan? interval = null)
    {
        _client = client;
        _timer = new PeriodicTimer(interval ?? TimeSpan.FromSeconds(3));
        _uiContext = SynchronizationContext.Current;
    }

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        _ = LoopAsync(_cts.Token);
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        _cts = null;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        // immediate check
        await CheckOnceAsync();

        while (await _timer.WaitForNextTickAsync(ct))
            await CheckOnceAsync();
    }

    private async Task CheckOnceAsync()
    {
        try
        {
            // lightweight "ping" to the service
            _ = await _client.GetDashboardStatsAsync();
            SetState(online: true, lastError: null);
        }
        catch (Exception ex)
        {
            SetState(online: false, lastError: ex.Message);
        }
    }

    private void SetState(bool online, string? lastError)
    {
        if (_uiContext is null || SynchronizationContext.Current == _uiContext)
        {
            IsOnline = online;
            LastError = lastError;
            return;
        }

        _uiContext.Post(_ =>
        {
            IsOnline = online;
            LastError = lastError;
        }, null);
    }

    public void Dispose()
    {
        Stop();
        _timer.Dispose();
    }
}
