using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace RdpShield.Manager.Converters;

public sealed class EventTypeBrushConverter : IValueConverter
{
    private static readonly Brush Red = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xD6, 0x52, 0x4B));
    private static readonly Brush Green = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x1E, 0xA6, 0x5B));
    private static readonly Brush Blue = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x3A, 0x78, 0xE9));
    private static readonly Brush Yellow = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xD9, 0xA4, 0x00));
    private static readonly Brush Neutral = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x7E, 0x8C, 0x99));

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var type = value?.ToString();

        return type switch
        {
            "IpBanned" or "FirewallError" => Red,
            "IpUnbanned" => Green,
            "AuthFailedDetected" => Yellow,
            "AllowlistUpdated" or "SettingsUpdated" or "ServiceStarted" or "ServiceStopping" => Blue,
            _ => Neutral
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
