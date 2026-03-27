using Microsoft.UI.Xaml.Data;
using RdpShield.Api;

namespace RdpShield.Manager.Converters;

public sealed class SimplifiedEventMessageConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not EventDto evt)
            return value?.ToString() ?? string.Empty;

        return evt.Type switch
        {
            "IpBanned" => "Banned",
            "IpUnbanned" => "Unbanned",
            "FirewallError" => "Firewall error",
            "AuthFailedDetected" => "Failed authentication attempt",
            "AllowlistUpdated" => "Allowlist updated",
            "SettingsUpdated" => "Settings updated",
            "ServiceStarted" => "Service started",
            "ServiceStopping" => "Service stopping",
            _ => evt.Message
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
