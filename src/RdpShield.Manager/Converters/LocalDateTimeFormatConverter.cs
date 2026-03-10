using System;
using Microsoft.UI.Xaml.Data;

namespace RdpShield.Manager.Converters;

public sealed class LocalDateTimeFormatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is null) return "";

        DateTime dt = value switch
        {
            DateTimeOffset dto => dto.LocalDateTime,
            DateTime d => d,
            _ => DateTime.MinValue
        };

        var format = parameter as string;
        if (string.IsNullOrWhiteSpace(format))
            format = "dd-MMM-yy HH:mm:ss";

        return dt.ToString(format);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
