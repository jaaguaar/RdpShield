using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RdpShield.Api;
using RdpShield.Api.Client;
using RdpShield.Manager.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.ServiceProcess;
using System.Threading;

namespace RdpShield.Manager.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject, IDisposable
{
    private const string ServiceName = "RdpShield";
    private const string ServiceProcessName = "RdpShield.Service";
    private const int RecentEventsRawPageSize = 200;
    private const int RecentEventsTargetChunk = 20;

    private static bool IsDashboardEvent(EventDto evt)
        => evt.Type is "IpBanned" or "IpUnbanned" or "FirewallError";

    private readonly IRdpShieldClient _client = RdpShieldClientFactory.Create();
    private readonly SemaphoreSlim _snapshotLock = new(1, 1);
    private bool _subscribed;
    private CancellationTokenSource? _liveCts;
    private Task? _liveTask;
    private DateTime? _serviceStartLocal;
    private int _statusTick;
    private int _recentEventsSkip;
    private readonly HashSet<string> _recentSeen = new(StringComparer.Ordinal);

    private int _activeBansCount;
    public int ActiveBansCount { get => _activeBansCount; set => SetProperty(ref _activeBansCount, value); }

    public ServiceConnectionState Connection => AppServices.Connection;

    private bool _isServiceRunning;
    public bool IsServiceRunning
    {
        get => _isServiceRunning;
        set
        {
            if (SetProperty(ref _isServiceRunning, value))
            {
                StartServiceCommand.NotifyCanExecuteChanged();
                StopServiceCommand.NotifyCanExecuteChanged();
                UnbanIpCommand.NotifyCanExecuteChanged();
                AddIpToAllowlistCommand.NotifyCanExecuteChanged();
                BlockIpCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private int _failedAttemptsLast10m;
    public int FailedAttemptsLast10m { get => _failedAttemptsLast10m; set => SetProperty(ref _failedAttemptsLast10m, value); }

    private string? _lastBannedIp;
    public string? LastBannedIp { get => _lastBannedIp; set => SetProperty(ref _lastBannedIp, value); }

    private DateTimeOffset? _lastBannedAtUtc;
    public DateTimeOffset? LastBannedAtUtc { get => _lastBannedAtUtc; set => SetProperty(ref _lastBannedAtUtc, value); }

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    private string? _error;
    public string? Error { get => _error; set => SetProperty(ref _error, value); }

    private string? _quickActionStatus;
    public string? QuickActionStatus { get => _quickActionStatus; set => SetProperty(ref _quickActionStatus, value); }

    private string? _allowlistIpInput;
    public string? AllowlistIpInput
    {
        get => _allowlistIpInput;
        set
        {
            if (SetProperty(ref _allowlistIpInput, value))
                AddIpToAllowlistCommand.NotifyCanExecuteChanged();
        }
    }

    private string? _unbanIpInput;
    public string? UnbanIpInput
    {
        get => _unbanIpInput;
        set
        {
            if (SetProperty(ref _unbanIpInput, value))
                UnbanIpCommand.NotifyCanExecuteChanged();
        }
    }

    private string? _blockIpInput;
    public string? BlockIpInput
    {
        get => _blockIpInput;
        set
        {
            if (SetProperty(ref _blockIpInput, value))
                BlockIpCommand.NotifyCanExecuteChanged();
        }
    }

    private string _serviceUptimeText = "Unknown";
    public string ServiceUptimeText { get => _serviceUptimeText; set => SetProperty(ref _serviceUptimeText, value); }

    public ObservableCollection<EventDto> RecentEvents { get; } = new();
    private DateTimeOffset _latestImportantTsUtc = DateTimeOffset.MinValue;

    private bool _isLoadingMoreRecentEvents;
    public bool IsLoadingMoreRecentEvents
    {
        get => _isLoadingMoreRecentEvents;
        set
        {
            if (SetProperty(ref _isLoadingMoreRecentEvents, value))
            {
                LoadMoreRecentEventsCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(RecentEventsFooterText));
            }
        }
    }

    private bool _hasMoreRecentEvents = true;
    public bool HasMoreRecentEvents
    {
        get => _hasMoreRecentEvents;
        set
        {
            if (SetProperty(ref _hasMoreRecentEvents, value))
            {
                LoadMoreRecentEventsCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(RecentEventsFooterText));
            }
        }
    }

    public string RecentEventsFooterText
        => HasMoreRecentEvents
            ? $"Loaded {RecentEvents.Count} events"
            : $"Loaded {RecentEvents.Count} events. No more events.";

    public void Start()
    {
        if (_subscribed) return;
        _subscribed = true;

        AppServices.Events.EventReceived += OnEvent;
        Connection.PropertyChanged += Connection_PropertyChanged;

        _ = LoadSnapshotAsync(includeEvents: true, showLoading: true);
        _ = RefreshServiceStateAsync(forceProcessScan: true);

        _liveCts = new CancellationTokenSource();
        _liveTask = RunLiveUpdatesAsync(_liveCts.Token);
    }

    public void Stop()
    {
        if (!_subscribed) return;
        _subscribed = false;

        AppServices.Events.EventReceived -= OnEvent;
        Connection.PropertyChanged -= Connection_PropertyChanged;

        _liveCts?.Cancel();
        _liveCts?.Dispose();
        _liveCts = null;
        _liveTask = null;
    }

    private void Connection_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ServiceConnectionState.IsOnline))
            _ = RefreshServiceStateAsync(forceProcessScan: true);
    }

    private async Task LoadSnapshotAsync(bool includeEvents, bool showLoading, CancellationToken ct = default)
    {
        if (!await _snapshotLock.WaitAsync(0, ct))
            return;

        try
        {
            if (showLoading)
                IsLoading = true;
            Error = null;

            var stats = await _client.GetDashboardStatsAsync(ct);
            ActiveBansCount = stats.ActiveBansCount;
            FailedAttemptsLast10m = stats.FailedAttemptsLast10m;
            LastBannedIp = stats.LastBannedIp;
            LastBannedAtUtc = stats.LastBannedAtUtc;

            if (!includeEvents)
                return;

            RecentEvents.Clear();
            _recentSeen.Clear();
            _recentEventsSkip = 0;
            HasMoreRecentEvents = true;
            _latestImportantTsUtc = DateTimeOffset.MinValue;
            OnPropertyChanged(nameof(RecentEventsFooterText));

            await LoadMoreRecentEventsCoreAsync(RecentEventsTargetChunk, ct);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            if (showLoading)
                IsLoading = false;
            _snapshotLock.Release();
        }
    }

    private void OnEvent(EventDto evt)
    {
        if (!IsDashboardEvent(evt))
            return;
        if (evt.TsUtc <= _latestImportantTsUtc)
            return;

        _latestImportantTsUtc = evt.TsUtc;
        Error = null;

        var key = BuildEventKey(evt);
        if (_recentSeen.Add(key))
        {
            RecentEvents.Insert(0, evt);
            OnPropertyChanged(nameof(RecentEventsFooterText));
        }

        if (evt.Type == "IpBanned")
        {
            LastBannedIp = evt.Ip;
            LastBannedAtUtc = evt.TsUtc;
            ActiveBansCount += 1;
        }
        else if (evt.Type == "IpUnbanned" && ActiveBansCount > 0)
        {
            ActiveBansCount -= 1;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartService))]
    private async Task StartServiceAsync()
    {
        try
        {
            Error = null;
            QuickActionStatus = null;

            await Task.Run(() =>
            {
                using var sc = new ServiceController(ServiceName);
                sc.Refresh();
                if (sc.Status == ServiceControllerStatus.Running)
                    return;

                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
            });

            QuickActionStatus = "Service started.";
            await RefreshServiceStateAsync(forceProcessScan: true);
        }
        catch (Exception ex)
        {
            Error = $"Unable to start service: {ex.Message}";
        }
    }

    private bool CanStartService() => !IsServiceRunning;

    [RelayCommand(CanExecute = nameof(CanStopService))]
    private async Task StopServiceAsync()
    {
        try
        {
            Error = null;
            QuickActionStatus = null;

            await Task.Run(() =>
            {
                using var sc = new ServiceController(ServiceName);
                sc.Refresh();
                if (sc.Status == ServiceControllerStatus.Stopped)
                    return;

                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
            });

            QuickActionStatus = "Service stopped.";
            await RefreshServiceStateAsync(forceProcessScan: true);
        }
        catch (Exception ex)
        {
            Error = $"Unable to stop service: {ex.Message}";
        }
    }

    private bool CanStopService() => IsServiceRunning;

    [RelayCommand(CanExecute = nameof(CanUnbanIp))]
    private async Task UnbanIpAsync()
    {
        if (!IsServiceRunning)
            return;

        var value = (UnbanIpInput ?? string.Empty).Trim();
        if (!IPAddress.TryParse(value, out _))
        {
            QuickActionStatus = "Invalid IP.";
            return;
        }

        try
        {
            Error = null;
            QuickActionStatus = null;

            await _client.UnbanIpAsync(value);
            QuickActionStatus = $"Unblocked {value}.";
        }
        catch (Exception ex)
        {
            Error = $"Unable to unban IP: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanAllowIp))]
    private async Task AddIpToAllowlistAsync()
    {
        if (!IsServiceRunning)
            return;

        var value = (AllowlistIpInput ?? string.Empty).Trim();
        if (!IsValidIpOrCidr(value))
        {
            QuickActionStatus = "Invalid IP/CIDR.";
            return;
        }

        try
        {
            Error = null;
            QuickActionStatus = null;

            await _client.AddAllowlistEntryAsync(value, "Added from Dashboard quick action");
            QuickActionStatus = $"Allowed {value}.";
        }
        catch (Exception ex)
        {
            Error = $"Unable to add IP to allowlist: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanBlockIp))]
    private async Task BlockIpAsync()
    {
        if (!IsServiceRunning)
            return;

        var value = (BlockIpInput ?? string.Empty).Trim();
        if (!IPAddress.TryParse(value, out _))
        {
            QuickActionStatus = "Invalid IP.";
            return;
        }

        try
        {
            Error = null;
            QuickActionStatus = null;

            await _client.BlockIpAsync(value, "Manual block (Dashboard quick action)");
            QuickActionStatus = $"Blocked {value}.";
        }
        catch (Exception ex)
        {
            Error = $"Unable to block IP: {ex.Message}";
        }
    }

    private bool CanUnbanIp() => IsServiceRunning && !string.IsNullOrWhiteSpace(UnbanIpInput);
    private bool CanAllowIp() => IsServiceRunning && !string.IsNullOrWhiteSpace(AllowlistIpInput);
    private bool CanBlockIp() => IsServiceRunning && !string.IsNullOrWhiteSpace(BlockIpInput);
    private bool CanLoadMoreRecentEvents() => HasMoreRecentEvents && !IsLoadingMoreRecentEvents;

    [RelayCommand(CanExecute = nameof(CanLoadMoreRecentEvents))]
    private async Task LoadMoreRecentEventsAsync(CancellationToken ct = default)
    {
        await LoadMoreRecentEventsCoreAsync(RecentEventsTargetChunk, ct);
    }

    private async Task LoadMoreRecentEventsCoreAsync(int targetImportantCount, CancellationToken ct)
    {
        if (!HasMoreRecentEvents || IsLoadingMoreRecentEvents)
            return;

        try
        {
            IsLoadingMoreRecentEvents = true;
            var added = 0;

            while (HasMoreRecentEvents && added < targetImportantCount)
            {
                var raw = await _client.GetRecentEventsAsync(RecentEventsRawPageSize, ct, _recentEventsSkip);
                _recentEventsSkip += raw.Count;
                HasMoreRecentEvents = raw.Count == RecentEventsRawPageSize;

                foreach (var evt in raw.Where(IsDashboardEvent).OrderByDescending(x => x.TsUtc))
                {
                    var key = BuildEventKey(evt);
                    if (!_recentSeen.Add(key))
                        continue;

                    RecentEvents.Add(evt);
                    if (evt.TsUtc > _latestImportantTsUtc)
                        _latestImportantTsUtc = evt.TsUtc;
                    added++;
                }

                if (raw.Count == 0)
                    break;
            }
        }
        finally
        {
            IsLoadingMoreRecentEvents = false;
            OnPropertyChanged(nameof(RecentEventsFooterText));
        }
    }

    private static bool IsValidIpOrCidr(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var slash = input.IndexOf('/');
        if (slash <= 0 || slash == input.Length - 1)
            return IPAddress.TryParse(input, out _);

        var ipPart = input[..slash].Trim();
        var prefixPart = input[(slash + 1)..].Trim();
        if (!IPAddress.TryParse(ipPart, out var ip))
            return false;
        if (!int.TryParse(prefixPart, out var prefix))
            return false;

        var max = ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
        return prefix >= 0 && prefix <= max;
    }

    private async Task RefreshServiceStateAsync(bool forceProcessScan = false)
    {
        try
        {
            var state = await Task.Run(() =>
            {
                using var sc = new ServiceController(ServiceName);
                sc.Refresh();
                var running = sc.Status == ServiceControllerStatus.Running;

                DateTime? start = null;
                if (running && (forceProcessScan || !_serviceStartLocal.HasValue))
                {
                    var proc = Process.GetProcessesByName(ServiceProcessName)
                        .OrderBy(p => p.StartTime)
                        .FirstOrDefault();
                    start = proc?.StartTime;
                }

                return (running, start);
            });

            IsServiceRunning = state.running;
            if (state.running)
            {
                if (state.start.HasValue)
                    _serviceStartLocal = state.start;

                ServiceUptimeText = _serviceStartLocal.HasValue
                    ? FormatUptime(DateTime.Now - _serviceStartLocal.Value)
                    : "Running (uptime unavailable)";
            }
            else
            {
                _serviceStartLocal = null;
                ServiceUptimeText = "Service is stopped.";
            }
        }
        catch
        {
            IsServiceRunning = false;
            _serviceStartLocal = null;
            ServiceUptimeText = "Service status unavailable.";
        }
    }

    private static string FormatUptime(TimeSpan t)
    {
        if (t.TotalDays >= 1)
            return $"{(int)t.TotalDays}d {t.Hours:00}h {t.Minutes:00}m {t.Seconds:00}s";
        if (t.TotalHours >= 1)
            return $"{(int)t.TotalHours}h {t.Minutes:00}m {t.Seconds:00}s";
        return $"{Math.Max(0, t.Minutes):00}m {Math.Max(0, t.Seconds):00}s";
    }

    private async Task RunLiveUpdatesAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                _statusTick++;

                // Keep uptime ticking smoothly.
                if (IsServiceRunning && _serviceStartLocal.HasValue)
                    ServiceUptimeText = FormatUptime(DateTime.Now - _serviceStartLocal.Value);

                // Sync status from SCM regularly, so UI stays correct even after external service actions.
                if (_statusTick % 3 == 0)
                    await RefreshServiceStateAsync();

                // Keep summary metrics fresh without reloading the recent events list.
                if (_statusTick % 5 == 0)
                    await LoadSnapshotAsync(includeEvents: false, showLoading: false, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal on stop.
        }
    }

    private static string BuildEventKey(EventDto evt)
        => $"{evt.TsUtc.UtcTicks}|{evt.Type}|{evt.Ip}|{evt.Message}";

    public void Dispose() => Stop();
}
