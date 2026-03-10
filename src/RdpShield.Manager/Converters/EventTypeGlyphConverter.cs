using Microsoft.UI.Xaml.Data;

namespace RdpShield.Manager.Converters;

public sealed class EventTypeGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var type = value?.ToString();

        // Segoe MDL2 Assets glyphs
        return type switch
        {
            "IpBanned" => "\uE7BA", // warning/error
            "FirewallError" => "\uEA39", // blocked/error
            "IpUnbanned" => "\uE73E", // check mark
            "AuthFailedDetected" => "\uE7BA", // warning
            "AllowlistUpdated" => "\uE8D7", // list/update
            "SettingsUpdated" => "\uE713", // settings gear
            "ServiceStarted" => "\uE768", // play
            "ServiceStopping" => "\uE71A", // stop
            _ => "\uE946" // info
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
