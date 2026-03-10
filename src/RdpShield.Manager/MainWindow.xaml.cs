using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RdpShield.Manager.Services;
using RdpShield.Manager.Views;
using System.IO;
using Windows.Graphics;

namespace RdpShield.Manager;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        TrySetSize(1100, 720);

        AppServices.Events.Initialize(DispatcherQueue);
        AppServices.Events.Start();
        AppServices.Connection.Start();

        Closed += (_, _) =>
        {
            try { AppServices.Connection.Stop(); } catch { }
            try { AppServices.Events.Stop(); } catch { }
        };

        Nav.SelectionChanged += Nav_SelectionChanged;

        Nav.SelectedItem = Nav.MenuItems[0];
        NavigateByTag("dashboard");
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
            return;

        if (args.SelectedItemContainer?.Tag is string tag)
            NavigateByTag(tag);
    }

    private void NavigateByTag(string tag)
    {
        switch (tag)
        {
            case "dashboard":
                ContentFrame.Navigate(typeof(DashboardPage));
                break;
            case "allowlist":
                ContentFrame.Navigate(typeof(AllowlistPage));
                break;
            case "activity":
                ContentFrame.Navigate(typeof(ActivityPage));
                break;
            case "bans":
                ContentFrame.Navigate(typeof(BansPage));
                break;
            case "settings":
                ContentFrame.Navigate(typeof(SettingsPage));
                break;
            default:
                ContentFrame.Navigate(typeof(DashboardPage));
                break;
        }
    }

    private void TrySetSize(int width, int height)
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var id = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(id);
            appWindow.Resize(new SizeInt32(width, height));

            // Keep a branded icon in taskbar/titlebar for unpackaged WinUI app.
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "RdpShield.ico");
            if (File.Exists(iconPath))
                appWindow.SetIcon(iconPath);
        }
        catch
        {
        }
    }
}
