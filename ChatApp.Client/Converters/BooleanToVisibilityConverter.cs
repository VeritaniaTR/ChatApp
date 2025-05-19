using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ChatApp.Client.Converters // ВАЖЛИВО: правильний простір імен
{
    public class BooleanToVisibilityConverter : IValueConverter // ВАЖЛИВО: public class
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool boolValue = false;
            if (value is bool b)
            {
                boolValue = b;
            }

            string direction = parameter as string;
            if (direction != null && direction.Equals("Inverse", StringComparison.OrdinalIgnoreCase))
            {
                boolValue = !boolValue;
            }
            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}