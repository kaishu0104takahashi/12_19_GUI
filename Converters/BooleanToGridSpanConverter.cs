using System;
using System.Globalization;
using Avalonia.Data.Converters;
namespace GUI_12_19.Converters;
public class BooleanToGridSpanConverter : IValueConverter {
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
        if (value is bool isGalleryActive) return isGalleryActive ? 2 : 1;
        return 1;
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Avalonia.Data.BindingOperations.DoNothing;
}
