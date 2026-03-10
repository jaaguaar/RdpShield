using Microsoft.UI.Xaml.Data;

namespace RdpShield.Manager.Converters;

public sealed class ServiceRunningTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => (value as bool?) == true ? "Service running" : "Service stopped";

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
