using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using Windows.UI;

namespace RdpShield.Manager.Converters;

public sealed class OnlineBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var online = value is bool b && b;
        return new SolidColorBrush(online ? Color.FromArgb(255, 22, 143, 78) : Color.FromArgb(255, 197, 59, 53));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
