using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace FilesMate.App.Controls.Navigation;

public sealed class BoolToHorizontalAlignmentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? HorizontalAlignment.Center : HorizontalAlignment.Left;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class CompactThicknessConverter : IValueConverter
{
    public Thickness CompactValue { get; set; }

    public Thickness ExpandedValue { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? CompactValue : ExpandedValue;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
