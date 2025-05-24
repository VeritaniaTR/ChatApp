using System; 
using System.Globalization;
using System.Windows.Data;

namespace ChatApp.Client.Converters
{
    public class StringToBooleanConverterForNullOrEmpty : IValueConverter
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