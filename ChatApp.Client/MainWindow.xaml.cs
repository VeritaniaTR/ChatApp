using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data; // Потрібно для IValueConverter
using System.Windows.Input;
using ChatApp.Client.ViewModels;

namespace ChatApp.Client.Views // Або ваш актуальний простір імен
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // DataContext може бути вже встановлений в XAML
            // if (DataContext == null)
            // {
            //     DataContext = new MainWindowViewModel();
            // }


            this.Loaded += (s, e) =>
            {
                if (DataContext is MainWindowViewModel viewModel)
                {
                    viewModel.ChatMessages.CollectionChanged += (sender, args) =>
                    {
                        if (viewModel.ChatMessages.Count > 0 && ChatScrollViewer != null)
                        {
                            ChatScrollViewer.ScrollToBottom();
                        }
                    };
                }
            };

            this.Closing += async (sender, e) =>
            {
                if (DataContext is MainWindowViewModel viewModel)
                {
                    if (viewModel.IsConnected)
                    {
                        if (viewModel.DisconnectCommand.CanExecute(null))
                        {
                            viewModel.DisconnectCommand.Execute(null);
                        }
                        await Task.Delay(250);
                    }
                }
            };
        }

        private void MessageTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (DataContext is MainWindowViewModel viewModel)
                {
                    if (viewModel.SendCommand.CanExecute(null))
                    {
                        viewModel.SendCommand.Execute(null);
                    }
                }
            }
        }
    }

    // --- КОНВЕРТЕРИ ---
    // Якщо вони оголошені тут

    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool boolValue = false;
            if (value is bool b) // Використання pattern matching для безпечного приведення типів
            {
                boolValue = b;
            }

            string direction = parameter as string;
            if (direction != null && direction.Equals("Inverse", StringComparison.OrdinalIgnoreCase))
            {
                boolValue = !boolValue;
            }

            return boolValue ? Visibility.Visible : Visibility.Collapsed; // За замовчуванням Collapsed краще для компонування
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

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