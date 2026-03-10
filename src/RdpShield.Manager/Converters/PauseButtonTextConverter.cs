using Microsoft.UI.Xaml.Data;

namespace RdpShield.Manager.Converters;

public sealed class PauseButtonTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is bool b && b ? "Resume" : "Pause";

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
