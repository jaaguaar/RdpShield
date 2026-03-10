using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using RdpShield.Manager.ViewModels;

namespace RdpShield.Manager.Views;

public sealed partial class AllowlistPage : Page
{
    private ScrollViewer? _listScrollViewer;

    public AllowlistPage()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        (DataContext as AllowlistViewModel)?.Start();

        _listScrollViewer = FindDescendant<ScrollViewer>(AllowlistList);
        if (_listScrollViewer is not null)
            _listScrollViewer.ViewChanged += ListScrollViewer_ViewChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_listScrollViewer is not null)
            _listScrollViewer.ViewChanged -= ListScrollViewer_ViewChanged;
        _listScrollViewer = null;

        (DataContext as AllowlistViewModel)?.Stop();
    }

    private void ListScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (sender is not ScrollViewer sv || DataContext is not AllowlistViewModel vm)
            return;

        var threshold = 80.0;
        if (sv.ScrollableHeight <= 0)
            return;

        if (sv.VerticalOffset >= sv.ScrollableHeight - threshold && vm.LoadMoreAllowlistCommand.CanExecute(null))
            vm.LoadMoreAllowlistCommand.Execute(null);
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match)
            return match;

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            var result = FindDescendant<T>(child);
            if (result is not null)
                return result;
        }

        return null;
    }
}
