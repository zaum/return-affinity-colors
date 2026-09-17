using System.Globalization;
using Avalonia.Data.Converters;

namespace ReturnColors.UI;

internal sealed class LogLevelToIconConverter : IValueConverter
{
    public static readonly LogLevelToIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is LogLevel level && level == LogLevel.Error)
            return "\u00D7";
        return "";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
