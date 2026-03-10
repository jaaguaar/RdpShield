using Microsoft.UI.Xaml.Data;

namespace RdpShield.Manager.Converters;

public sealed class OnlineTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is bool b && b ? "Online" : "Offline";

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
