using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RdpShield.Api;
using RdpShield.Api.Client;
using RdpShield.Manager.Services;
using System.Collections.ObjectModel;
using System.Net;
using System.Threading;

namespace RdpShield.Manager.ViewModels;

public sealed partial class BansViewModel : ObservableObject, IDisposable
{
    private const int PageSize = 200;

    private readonly IRdpShieldClient _client = RdpShieldClientFactory.Create();
    private readonly SemaphoreSlim _opLock = new(1, 1);
    private bool _subscribed;
    private bool _pendingReload;
    private int _skipFromNewest;
    public ServiceConnectionState Connection => AppServices.Connection;

    public ObservableCollection<BanDto> ActiveBans { get; } = new();

    private string? _filter;
    public string? Filter
    {
        get => _filter;
        set { if (SetProperty(ref _filter, value)) ApplyFilter(); }
    }

    private string? _error;
    public string? Error { get => _error; set => SetProperty(ref _error, value); }

    private string? _newBlockIp;
    public string? NewBlockIp
    {
        get => _newBlockIp;
        set
        {
            if (SetProperty(ref _newBlockIp, value))
                BlockIpCommand.NotifyCanExecuteChanged();
        }
    }

    private string? _newBlockReason;
    public string? NewBlockReason
    {
        get => _newBlockReason;
        set => SetProperty(ref _newBlockReason, value);
    }

    private readonly List<BanDto> _buffer = new();

    private bool _isLoadingMore;
    public bool IsLoadingMore
    {
        get => _isLoadingMore;
        set
        {
            if (SetProperty(ref _isLoadingMore, value))
            {
                LoadMoreBansCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(BansFooterText));
            }
        }
    }

    private bool _hasMoreBans = true;
    public bool HasMoreBans
    {
        get => _hasMoreBans;
        set
        {
            if (SetProperty(ref _hasMoreBans, value))
            {
                LoadMoreBansCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(BansFooterText));
            }
        }
    }

    public string BansFooterText
        => HasMoreBans
            ? $"Loaded {_buffer.Count} bans"
            : $"Loaded {_buffer.Count} bans. No more entries.";

    public void Start()
    {
        if (_subscribed) return;
        _subscribed = true;

        AppServices.Events.EventReceived += OnEvent;
        _ = LoadInitialAsync();
    }

    public void Stop()
    {
        if (!_subscribed) return;
        _subscribed = false;

        AppServices.Events.EventReceived -= OnEvent;
    }

    private async Task LoadInitialAsync(CancellationToken ct = default)
    {
        if (!await _opLock.WaitAsync(0, ct))
        {
            _pendingReload = true;
            return;
        }

        try
        {
            do
            {
                _pendingReload = false;

                Error = null;
                _skipFromNewest = 0;
                _buffer.Clear();
                ActiveBans.Clear();
                HasMoreBans = true;
                OnPropertyChanged(nameof(BansFooterText));

                await LoadMoreBansCoreAsync(ct);
            }
            while (_pendingReload && !ct.IsCancellationRequested);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            _opLock.Release();
        }
    }

    [RelayCommand(CanExecute = nameof(CanLoadMoreBans))]
    private async Task LoadMoreBansAsync(CancellationToken ct = default)
    {
        if (!CanLoadMoreBans())
            return;
        if (!await _opLock.WaitAsync(0, ct))
            return;

        try
        {
            await LoadMoreBansCoreAsync(ct);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            _opLock.Release();
        }
    }

    private bool CanLoadMoreBans() => HasMoreBans && !IsLoadingMore;

    private void OnEvent(EventDto evt)
    {
        // Best-effort live updates
        if (evt.Type == "IpBanned" && !string.IsNullOrWhiteSpace(evt.Ip))
        {
            _ = LoadInitialAsync();
        }
        else if (evt.Type == "IpUnbanned" && !string.IsNullOrWhiteSpace(evt.Ip))
        {
            _ = LoadInitialAsync();
        }
    }

    private void ApplyFilter()
    {
        var q = (Filter ?? "").Trim();

        ActiveBans.Clear();

        IEnumerable<BanDto> src = _buffer;
        if (!string.IsNullOrWhiteSpace(q))
        {
            src = src.Where(b =>
                b.Ip.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                b.Reason.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                b.Source.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var b in src)
            ActiveBans.Add(b);
    }

    [RelayCommand]
    private async Task UnbanAsync(BanDto ban)
    {
        try
        {
            Error = null;
            await _client.UnbanIpAsync(ban.Ip);
            await LoadInitialAsync();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }

    private async Task LoadMoreBansCoreAsync(CancellationToken ct)
    {
        IsLoadingMore = true;
        Error = null;

        try
        {
            var bans = await _client.GetActiveBansAsync(ct, PageSize, _skipFromNewest);
            _skipFromNewest += bans.Count;
            HasMoreBans = bans.Count == PageSize;

            _buffer.AddRange(bans);
            ApplyFilter();
            OnPropertyChanged(nameof(BansFooterText));
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanBlockIp))]
    private async Task BlockIpAsync()
    {
        if (!TryNormalizeIp(NewBlockIp, out var ip))
        {
            Error = "Invalid IP.";
            return;
        }

        try
        {
            Error = null;
            var reason = string.IsNullOrWhiteSpace(NewBlockReason)
                ? "Manual block (Bans page)"
                : NewBlockReason.Trim();

            await _client.BlockIpAsync(ip, reason);
            NewBlockIp = null;
            NewBlockReason = null;
            await LoadInitialAsync();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }

    private bool CanBlockIp() => !string.IsNullOrWhiteSpace(NewBlockIp);

    private static bool TryNormalizeIp(string? input, out string ip)
    {
        ip = (input ?? string.Empty).Trim();
        return IPAddress.TryParse(ip, out _);
    }

    public void Dispose() => Stop();
}
