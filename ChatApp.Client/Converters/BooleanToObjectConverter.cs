using System;
using System.Globalization;
using System.Windows.Data; // Для IValueConverter
using System.Windows.Markup; // Для MarkupExtension, якщо потрібно (але тут не використовується)

namespace ChatApp.Client.Converters
{
    public class BooleanToObjectConverter : IValueConverter
    {
        public object TrueValue { get; set; }
        public object FalseValue { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                // Якщо параметр "Accent", то можливо інвертувати логіку або вибрати інші значення
                // Для простоти, поки що ігноруємо параметр
                return boolValue ? TrueValue : FalseValue;
            }
            return FalseValue; // Значення за замовчуванням, якщо вхідне значення не bool
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Зазвичай не потрібно для односторонньої конвертації
            throw new NotImplementedException();
        }
    }
}