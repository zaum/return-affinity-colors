using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ReturnColors.UI;

internal sealed class LogLevelToColorConverter : IValueConverter
{
    public static readonly LogLevelToColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is LogLevel level && level == LogLevel.Error)
            return new SolidColorBrush(Color.FromRgb(255, 90, 80));
        return new SolidColorBrush(Colors.White);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
