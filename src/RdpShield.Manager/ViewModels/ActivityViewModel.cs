using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RdpShield.Api;
using RdpShield.Api.Client;
using RdpShield.Manager.Services;
using System.Collections.ObjectModel;

namespace RdpShield.Manager.ViewModels;

public sealed partial class ActivityViewModel : ObservableObject, IDisposable
{
    private const int PageSize = 200;
    private const int MaxBuffer = 5000;
    private const int MaxPausedBuffer = 1000;

    private readonly IRdpShieldClient _client = RdpShieldClientFactory.Create();
    private bool _subscribed;
    private int _skipFromNewest;

    public ServiceConnectionState Connection => AppServices.Connection;
    public ObservableCollection<EventDto> Events { get; } = new();

    private readonly List<EventDto> _buffer = new();
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

    private readonly List<EventDto> _pausedBuffer = new();
    private readonly object _pauseLock = new();

    private string? _filterText;
    public string? FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value))
                ApplyFilter();
        }
    }

    private string? _error;
    public string? Error
    {
        get => _error;
        set => SetProperty(ref _error, value);
    }

    private bool _isPaused;
    public bool IsPaused
    {
        get => _isPaused;
        set
        {
            if (!SetProperty(ref _isPaused, value))
                return;

            if (!_isPaused)
                FlushPausedBuffer();
        }
    }

    private int _bufferedCount;
    public int BufferedCount
    {
        get => _bufferedCount;
        private set => SetProperty(ref _bufferedCount, value);
    }

    private bool _isLoadingMore;
    public bool IsLoadingMore
    {
        get => _isLoadingMore;
        set
        {
            if (SetProperty(ref _isLoadingMore, value))
            {
                LoadMoreEventsCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(EventsFooterText));
            }
        }
    }

    private bool _hasMoreEvents = true;
    public bool HasMoreEvents
    {
        get => _hasMoreEvents;
        set
        {
            if (SetProperty(ref _hasMoreEvents, value))
            {
                LoadMoreEventsCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(EventsFooterText));
            }
        }
    }

    public string EventsFooterText
        => HasMoreEvents
            ? $"Loaded {_buffer.Count} events"
            : $"Loaded {_buffer.Count} events. No more events.";

    [RelayCommand]
    private void TogglePause() => IsPaused = !IsPaused;

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
        try
        {
            Error = null;
            _skipFromNewest = 0;
            HasMoreEvents = true;
            IsPaused = false;

            lock (_pauseLock)
            {
                _pausedBuffer.Clear();
                BufferedCount = 0;
            }

            _buffer.Clear();
            _seen.Clear();
            Events.Clear();
            OnPropertyChanged(nameof(EventsFooterText));

            await LoadMoreEventsAsync(ct);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }

    private void OnEvent(EventDto evt)
    {
        Error = null;

        if (IsPaused)
        {
            lock (_pauseLock)
            {
                _pausedBuffer.Insert(0, evt);
                TrimList(_pausedBuffer, MaxPausedBuffer);
                BufferedCount = _pausedBuffer.Count;
            }
            return;
        }

        if (!TryTrack(evt))
            return;

        _buffer.Insert(0, evt);
        TrimList(_buffer, MaxBuffer);
        ApplyFilterIncremental(evt);
    }

    private void FlushPausedBuffer()
    {
        List<EventDto> toMerge;
        lock (_pauseLock)
        {
            if (_pausedBuffer.Count == 0)
            {
                BufferedCount = 0;
                return;
            }

            toMerge = _pausedBuffer.OrderByDescending(x => x.TsUtc).ToList();
            _pausedBuffer.Clear();
            BufferedCount = 0;
        }

        foreach (var evt in toMerge)
        {
            if (!TryTrack(evt))
                continue;
            _buffer.Insert(0, evt);
        }

        TrimList(_buffer, MaxBuffer);
        ApplyFilter();
    }

    [RelayCommand(CanExecute = nameof(CanLoadMoreEvents))]
    private async Task LoadMoreEventsAsync(CancellationToken ct = default)
    {
        if (!CanLoadMoreEvents())
            return;

        try
        {
            IsLoadingMore = true;
            Error = null;

            var page = await _client.GetRecentEventsAsync(PageSize, ct, _skipFromNewest);
            _skipFromNewest += page.Count;
            HasMoreEvents = page.Count == PageSize;

            foreach (var evt in page.OrderByDescending(x => x.TsUtc))
            {
                if (!TryTrack(evt))
                    continue;

                _buffer.Add(evt);
            }

            TrimList(_buffer, MaxBuffer);
            ApplyFilter();
            OnPropertyChanged(nameof(EventsFooterText));
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    private bool CanLoadMoreEvents() => HasMoreEvents && !IsLoadingMore;

    private bool TryTrack(EventDto evt)
    {
        var key = $"{evt.TsUtc.UtcTicks}|{evt.Type}|{evt.Ip}|{evt.Message}";
        return _seen.Add(key);
    }

    private void ApplyFilter()
    {
        var q = (FilterText ?? string.Empty).Trim();
        Events.Clear();

        IEnumerable<EventDto> src = _buffer;
        if (!string.IsNullOrWhiteSpace(q))
        {
            src = src.Where(e =>
                (e.Type?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (e.Message?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (e.Ip?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (e.Username?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (e.Level?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        foreach (var evt in src)
            Events.Add(evt);
    }

    private void ApplyFilterIncremental(EventDto evt)
    {
        var q = (FilterText ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(q))
        {
            Events.Insert(0, evt);
            return;
        }

        var ok =
            (evt.Type?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (evt.Message?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (evt.Ip?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (evt.Username?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (evt.Level?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);

        if (ok)
            Events.Insert(0, evt);
    }

    private static void TrimList<T>(List<T> list, int max)
    {
        while (list.Count > max)
            list.RemoveAt(list.Count - 1);
    }

    public void Dispose() => Stop();
}
