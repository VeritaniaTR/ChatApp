using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ChatApp.Client.ViewModels;
using System.Collections.Specialized;
using System.Threading.Tasks;
using System.Windows.Data; // Додано для IValueConverter
using System.Globalization; // Додано для CultureInfo
using System; // Додано для Type

namespace ChatApp.Client.Views // <-- ПЕРЕКОНАЙТЕСЯ, ЩО ЦЕЙ ПРОСТІР ІМЕН ВІРНИЙ
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            this.Loaded += (s, e) =>
            {
                if (DataContext is MainWindowViewModel viewModel)
                {
                    viewModel.ChatMessages.CollectionChanged += (sender, args) =>
                    {
                        if (ChatListBox.Items.Count > 0)
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
                        viewModel.DisconnectCommand.Execute(null);
                        await Task.Delay(500); // Даємо час на відправку повідомлення про відключення
                    }
                }
            };
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.ConnectCommand.Execute(null);
            }
        }

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.SendCommand.Execute(viewModel.MessageToSend);
            }
        }

        private void MessageTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (DataContext is MainWindowViewModel viewModel)
                {
                    viewModel.SendCommand.Execute(viewModel.MessageToSend);
                }
            }
        }
    }

    // Клас конвертера BooleanToVisibilityConverter
    // ПОВИНЕН БУТИ PUBLIC І В ТОМУ Ж ПРОСТОРІ ІМЕН, ЩО Й MainWindow
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool boolValue = (bool)value;
            string visibilityParam = parameter as string;

            if (visibilityParam == "Hidden")
            {
                return boolValue ? Visibility.Visible : Visibility.Hidden;
            }
            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return DependencyProperty.UnsetValue;
        }
    }
}