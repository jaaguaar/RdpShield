using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RdpShield.Api;
using RdpShield.Api.Client;
using RdpShield.Manager.Services;

namespace RdpShield.Manager.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IRdpShieldClient _client = RdpShieldClientFactory.Create();
    private const int MinAllowlistRefreshSeconds = 1;
    private const int MaxAllowlistRefreshSeconds = 3600;

    private SettingsDto? _baseline;
    private CancellationTokenSource? _statusCts;

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    private string? _status;
    public string? Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    private string? _error;
    public string? Error
    {
        get => _error;
        set => SetProperty(ref _error, value);
    }

    private bool _isDirty;
    public bool IsDirty
    {
        get => _isDirty;
        private set => SetProperty(ref _isDirty, value);
    }

    private int _attemptsThreshold = 3;
    public int AttemptsThreshold
    {
        get => _attemptsThreshold;
        set
        {
            if (SetProperty(ref _attemptsThreshold, value))
                RecalcDirty();
        }
    }

    private int _windowSeconds = 120;
    public int WindowSeconds
    {
        get => _windowSeconds;
        set
        {
            if (SetProperty(ref _windowSeconds, value))
                RecalcDirty();
        }
    }

    private int _banMinutes = 120;
    public int BanMinutes
    {
        get => _banMinutes;
        set
        {
            if (SetProperty(ref _banMinutes, value))
                RecalcDirty();
        }
    }

    private bool _enableFirewall = true;
    public bool EnableFirewall
    {
        get => _enableFirewall;
        set
        {
            if (SetProperty(ref _enableFirewall, value))
                RecalcDirty();
        }
    }

    private string _firewallRulePrefix = "RdpShield Block";
    public string FirewallRulePrefix
    {
        get => _firewallRulePrefix;
        set
        {
            if (SetProperty(ref _firewallRulePrefix, value))
                RecalcDirty();
        }
    }

    private int _rdpPort = 3389;
    public int RdpPort
    {
        get => _rdpPort;
        set
        {
            if (SetProperty(ref _rdpPort, value))
                RecalcDirty();
        }
    }

    private int _allowlistRefreshSeconds = 10;
    public int AllowlistRefreshSeconds
    {
        get => _allowlistRefreshSeconds;
        set
        {
            if (SetProperty(ref _allowlistRefreshSeconds, value))
                RecalcDirty();
        }
    }

    public async Task LoadAsync()
    {
        try
        {
            IsLoading = true;
            Error = null;
            Status = null;

            var s = await _client.GetSettingsAsync();
            AttemptsThreshold = s.AttemptsThreshold;
            WindowSeconds = s.WindowSeconds;
            BanMinutes = s.BanMinutes;
            EnableFirewall = s.EnableFirewall;
            FirewallRulePrefix = s.FirewallRulePrefix;
            RdpPort = s.RdpPort;
            AllowlistRefreshSeconds = s.AllowlistRefreshSeconds;

            _baseline = new SettingsDto
            {
                AttemptsThreshold = AttemptsThreshold,
                WindowSeconds = WindowSeconds,
                BanMinutes = BanMinutes,
                EnableFirewall = EnableFirewall,
                FirewallRulePrefix = FirewallRulePrefix,
                RdpPort = RdpPort,
                AllowlistRefreshSeconds = AllowlistRefreshSeconds
            };

            RecalcDirty();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (AttemptsThreshold < 1 || AttemptsThreshold > 50)
        {
            Error = "Attempts threshold must be between 1 and 50.";
            return;
        }

        if (WindowSeconds < 10 || WindowSeconds > 3600)
        {
            Error = "Window seconds must be between 10 and 3600.";
            return;
        }

        if (BanMinutes < 1 || BanMinutes > 10080)
        {
            Error = "Ban minutes must be between 1 and 10080 (7 days).";
            return;
        }

        if (AllowlistRefreshSeconds < MinAllowlistRefreshSeconds || AllowlistRefreshSeconds > MaxAllowlistRefreshSeconds)
        {
            Error = $"Allowlist refresh must be between {MinAllowlistRefreshSeconds} and {MaxAllowlistRefreshSeconds} seconds.";
            return;
        }

        var firewallRulePrefix = (FirewallRulePrefix ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(firewallRulePrefix))
        {
            Error = "Firewall rule prefix cannot be empty.";
            return;
        }

        if (RdpPort < 1 || RdpPort > 65535)
        {
            Error = "RDP port must be between 1 and 65535.";
            return;
        }

        try
        {
            IsLoading = true;
            Error = null;
            Status = null;

            var dto = new SettingsDto
            {
                AttemptsThreshold = AttemptsThreshold,
                WindowSeconds = WindowSeconds,
                BanMinutes = BanMinutes,
                EnableFirewall = EnableFirewall,
                FirewallRulePrefix = firewallRulePrefix,
                RdpPort = RdpPort,
                AllowlistRefreshSeconds = AllowlistRefreshSeconds
            };

            await _client.UpdateSettingsAsync(dto);

            _baseline = new SettingsDto
            {
                AttemptsThreshold = AttemptsThreshold,
                WindowSeconds = WindowSeconds,
                BanMinutes = BanMinutes,
                EnableFirewall = EnableFirewall,
                FirewallRulePrefix = firewallRulePrefix,
                RdpPort = RdpPort,
                AllowlistRefreshSeconds = AllowlistRefreshSeconds
            };

            FirewallRulePrefix = firewallRulePrefix;

            RecalcDirty();

            ShowStatusFor("Saved", TimeSpan.FromSeconds(2.5));
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void RecalcDirty()
    {
        if (_baseline is null)
        {
            IsDirty = false;
            return;
        }

        IsDirty =
            AttemptsThreshold != _baseline.AttemptsThreshold ||
            WindowSeconds != _baseline.WindowSeconds ||
            BanMinutes != _baseline.BanMinutes ||
            EnableFirewall != _baseline.EnableFirewall ||
            !string.Equals((FirewallRulePrefix ?? string.Empty).Trim(), (_baseline.FirewallRulePrefix ?? string.Empty).Trim(), StringComparison.Ordinal) ||
            RdpPort != _baseline.RdpPort ||
            AllowlistRefreshSeconds != _baseline.AllowlistRefreshSeconds;
    }

    private void ShowStatusFor(string text, TimeSpan duration)
    {
        Status = text;

        try { _statusCts?.Cancel(); } catch { }
        _statusCts = new CancellationTokenSource();
        var ct = _statusCts.Token;

        // No Task.Run: keep UI context
        _ = ClearStatusLaterAsync(text, duration, ct);
    }

    private async Task ClearStatusLaterAsync(string text, TimeSpan duration, CancellationToken ct)
    {
        try
        {
            await Task.Delay(duration, ct);
        }
        catch
        {
            return;
        }

        if (!ct.IsCancellationRequested && Status == text)
            Status = null;
    }
}
