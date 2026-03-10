using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using RdpShield.Manager.ViewModels;
using System;

namespace RdpShield.Manager.Views;

public sealed partial class SettingsPage : Page
{
    private bool _loadedOnce;

    public SettingsPage()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (_loadedOnce) return;
            _loadedOnce = true;

            if (DataContext is SettingsViewModel vm)
                await vm.LoadAsync();
        };
    }

    private void OnDecreaseAttemptsThresholdClick(object sender, RoutedEventArgs e) => AdjustAttemptsThreshold(-1);
    private void OnIncreaseAttemptsThresholdClick(object sender, RoutedEventArgs e) => AdjustAttemptsThreshold(+1);
    private void OnDecreaseWindowSecondsClick(object sender, RoutedEventArgs e) => AdjustWindowSeconds(-10);
    private void OnIncreaseWindowSecondsClick(object sender, RoutedEventArgs e) => AdjustWindowSeconds(+10);
    private void OnDecreaseBanMinutesClick(object sender, RoutedEventArgs e) => AdjustBanMinutes(-1);
    private void OnIncreaseBanMinutesClick(object sender, RoutedEventArgs e) => AdjustBanMinutes(+1);

    private void AdjustAttemptsThreshold(int delta)
    {
        if (DataContext is not SettingsViewModel vm) return;
        vm.AttemptsThreshold = Math.Clamp(vm.AttemptsThreshold + delta, 1, 50);
    }

    private void AdjustWindowSeconds(int delta)
    {
        if (DataContext is not SettingsViewModel vm) return;
        vm.WindowSeconds = Math.Clamp(vm.WindowSeconds + delta, 10, 3600);
    }

    private void AdjustBanMinutes(int delta)
    {
        if (DataContext is not SettingsViewModel vm) return;
        vm.BanMinutes = Math.Clamp(vm.BanMinutes + delta, 1, 10080);
    }
}
