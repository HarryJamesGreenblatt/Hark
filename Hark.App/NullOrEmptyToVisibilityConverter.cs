using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Hark.App;

/// <summary>
/// Collapses an element when its bound value is null or an empty/whitespace string, otherwise shows
/// it. Used for the optional owner pill on a follow-up task in the structured recap.
/// </summary>
public sealed class NullOrEmptyToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
