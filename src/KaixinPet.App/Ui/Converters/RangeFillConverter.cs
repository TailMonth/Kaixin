using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace KaixinPet.App.Ui.Converters;

public sealed class RangeFillConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var value = ToDouble(values, 0);
        var minimum = ToDouble(values, 1);
        var maximum = ToDouble(values, 2);
        var ratio = maximum <= minimum ? 0 : Math.Clamp((value - minimum) / (maximum - minimum), 0, 1);
        if (string.Equals(parameter as string, "Inverse", StringComparison.OrdinalIgnoreCase))
        {
            ratio = 1 - ratio;
        }

        return new GridLength(ratio, GridUnitType.Star);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static double ToDouble(object[] values, int index) =>
        values.Length > index && values[index] is IConvertible convertible
            ? convertible.ToDouble(CultureInfo.InvariantCulture)
            : 0;
}
