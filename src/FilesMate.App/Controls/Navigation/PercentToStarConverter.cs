using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace FilesMate.App.Controls.Navigation;

public sealed class PercentToStarConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var used = value is int percent ? Math.Clamp(percent, 0, 100) : 0;
        var weight = Invert ? 100 - used : used;
        return new GridLength(weight, GridUnitType.Star);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
