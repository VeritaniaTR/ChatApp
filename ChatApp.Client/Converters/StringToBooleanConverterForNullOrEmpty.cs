using System.Globalization;
using System.Windows.Data;

using System;
using System.Globalization;
using System.Windows.Data;

namespace ChatApp.Client.Converters // ВАЖЛИВО: правильний простір імен
{
    public class StringToBooleanConverterForNullOrEmpty : IValueConverter // ВАЖЛИВО: public class
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string strValue = value as string;
            bool result = !string.IsNullOrEmpty(strValue);

            if (parameter is string paramStr && paramStr.Equals("Inverse", StringComparison.OrdinalIgnoreCase))
            {
                return !result;
            }
            return result;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}