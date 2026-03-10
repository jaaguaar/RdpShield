using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RdpShield.Api;
using RdpShield.Api.Client;
using RdpShield.Manager.Services;
using System.Collections.ObjectModel;
using System.Net;
using System.Threading;

namespace RdpShield.Manager.ViewModels;

public sealed partial class AllowlistViewModel : ObservableObject, IDisposable
{
    private const int PageSize = 200;

    private readonly IRdpShieldClient _client = RdpShieldClientFactory.Create();
    private readonly SemaphoreSlim _opLock = new(1, 1);
    private bool _subscribed;
    private bool _pendingReload;
    private int _skipFromStart;

    public ObservableCollection<AllowlistDto> Items { get; } = new();

    private string? _filter;
    public string? Filter
    {
        get => _filter;
        set { if (SetProperty(ref _filter, value)) ApplyFilter(); }
    }

    private string? _newEntry;
    public string? NewEntry
    {
        get => _newEntry;
        set => SetProperty(ref _newEntry, value);
    }

    private string? _newComment;
    public string? NewComment
    {
        get => _newComment;
        set => SetProperty(ref _newComment, value);
    }

    private string? _error;
    public string? Error { get => _error; set => SetProperty(ref _error, value); }

    private readonly List<AllowlistDto> _buffer = new();

    private bool _isLoadingMore;
    public bool IsLoadingMore
    {
        get => _isLoadingMore;
        set
        {
            if (SetProperty(ref _isLoadingMore, value))
            {
                LoadMoreAllowlistCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(AllowlistFooterText));
            }
        }
    }

    private bool _hasMoreEntries = true;
    public bool HasMoreEntries
    {
        get => _hasMoreEntries;
        set
        {
            if (SetProperty(ref _hasMoreEntries, value))
            {
                LoadMoreAllowlistCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(AllowlistFooterText));
            }
        }
    }

    public string AllowlistFooterText
        => HasMoreEntries
            ? $"Loaded {_buffer.Count} entries"
            : $"Loaded {_buffer.Count} entries. No more entries.";

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
                _skipFromStart = 0;
                _buffer.Clear();
                Items.Clear();
                HasMoreEntries = true;
                OnPropertyChanged(nameof(AllowlistFooterText));

                await LoadMoreAllowlistCoreAsync(ct);
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

    [RelayCommand(CanExecute = nameof(CanLoadMoreAllowlist))]
    private async Task LoadMoreAllowlistAsync(CancellationToken ct = default)
    {
        if (!CanLoadMoreAllowlist())
            return;
        if (!await _opLock.WaitAsync(0, ct))
            return;

        try
        {
            await LoadMoreAllowlistCoreAsync(ct);
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

    private bool CanLoadMoreAllowlist() => HasMoreEntries && !IsLoadingMore;

    private void OnEvent(EventDto evt)
    {
        // best-effort live refresh
        if (evt.Type == "AllowlistUpdated")
            _ = LoadInitialAsync();
    }

    private void ApplyFilter()
    {
        var q = (Filter ?? "").Trim();

        Items.Clear();

        IEnumerable<AllowlistDto> src = _buffer;
        if (!string.IsNullOrWhiteSpace(q))
        {
            src = src.Where(x =>
                x.Entry.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (x.Comment?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        foreach (var it in src)
            Items.Add(it);
    }

    private async Task LoadMoreAllowlistCoreAsync(CancellationToken ct)
    {
        IsLoadingMore = true;
        Error = null;

        try
        {
            var list = await _client.GetAllowlistAsync(ct, PageSize, _skipFromStart);
            _skipFromStart += list.Count;
            HasMoreEntries = list.Count == PageSize;

            _buffer.AddRange(list);
            ApplyFilter();
            OnPropertyChanged(nameof(AllowlistFooterText));
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    [RelayCommand]
    private async Task AddOrUpdateAsync()
    {
        var entry = (NewEntry ?? "").Trim();
        var comment = string.IsNullOrWhiteSpace(NewComment) ? null : NewComment.Trim();

        if (!IsValidEntry(entry))
        {
            Error = "Entry must be a valid IPv4 address (e.g. 1.2.3.4) or CIDR (e.g. 192.168.0.0/24).";
            return;
        }

        try
        {
            Error = null;
            await _client.AddAllowlistEntryAsync(entry, comment);
            NewEntry = "";
            NewComment = "";
            await LoadInitialAsync();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }

    [RelayCommand]
    private async Task RemoveAsync(AllowlistDto item)
    {
        try
        {
            Error = null;
            await _client.RemoveAllowlistEntryAsync(item.Entry);
            await LoadInitialAsync();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }

    private static bool IsValidEntry(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
            return false;

        // IP
        if (IPAddress.TryParse(entry, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            return true;

        // CIDR like 192.168.0.0/24
        var parts = entry.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 2 &&
            IPAddress.TryParse(parts[0], out var net) &&
            net.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
            int.TryParse(parts[1], out var prefix) &&
            prefix is >= 0 and <= 32)
            return true;

        return false;
    }

    public void Dispose() => Stop();
}
