using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FontSelector.Converters;

/// <summary>
/// Converts a theme key or boolean to a theme icon string.
/// </summary>
public class ThemeIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string key)
        {
            return key switch
            {
                "tal7aouy" => "🎨",
                "spinel" => "💎",
                "dark" => "🌙",
                "light" => "☀️",
                _ => "🎨"
            };
        }
        return value is true ? "☀️" : "🌙";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a double font size to a display string like "24 pt".
/// </summary>
public class FontSizeDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double size)
            return $"{size:F0} pt";
        return "-- pt";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a boolean to Visibility (True = Visible, False = Collapsed).
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isVisible = value is true;
        if (parameter is string p && p.Equals("Inverse", StringComparison.OrdinalIgnoreCase))
        {
            isVisible = !isVisible;
        }
        return isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
