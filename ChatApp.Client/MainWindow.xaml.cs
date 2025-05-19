using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation; // Для RequestNavigateEventArgs
using ChatApp.Client.ViewModels; // Переконайтеся, що цей простір імен правильний

namespace ChatApp.Client.Views // Переконайтеся, що цей простір імен правильний
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // DataContext встановлюється в XAML через <vm:MainWindowViewModel/>
            // Якщо виникають проблеми з цим, можна тимчасово встановити тут для діагностики:
            // if (this.DataContext == null)
            // {
            //    try
            //    {
            //        this.DataContext = new MainWindowViewModel();
            //    }
            //    catch (Exception ex)
            //    {
            //        MessageBox.Show("Помилка створення MainWindowViewModel в MainWindow.xaml.cs: " + ex.Message);
            //    }
            // }

            this.Loaded += (s, e) =>
            {
                if (this.DataContext is MainWindowViewModel viewModel)
                {
                    viewModel.ChatMessages.CollectionChanged += (sender, args) =>
                    {
                        if (viewModel.ChatMessages.Count > 0 && ChatScrollViewer != null)
                        {
                            // Загортаємо в Dispatcher на випадок, якщо CollectionChanged викликається з фонового потоку
                            ChatScrollViewer.Dispatcher.InvokeAsync(() => ChatScrollViewer.ScrollToBottom());
                        }
                    };
                }
                else
                {
                    // Це може статися, якщо DataContext не встановлено або має неправильний тип
                    Debug.WriteLine("[MainWindow.Loaded] DataContext не є MainWindowViewModel або null.");
                }
            };

            this.Closing += async (sender, e) =>
            {
                if (this.DataContext is MainWindowViewModel viewModel)
                {
                    if (viewModel.IsConnected)
                    {
                        if (viewModel.DisconnectCommand.CanExecute(null))
                        {
                            viewModel.DisconnectCommand.Execute(null);
                        }
                        // Даємо невелику затримку, щоб повідомлення про відключення встигло надіслатися
                        await Task.Delay(250);
                    }
                }
            };
        }

        // Обробник для надсилання повідомлення по натисканню Enter в TextBox
        private void MessageTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (this.DataContext is MainWindowViewModel viewModel) // this.DataContext, а не DataContext
                {
                    if (viewModel.SendCommand.CanExecute(null))
                    {
                        viewModel.SendCommand.Execute(null);
                    }
                }
            }
        }

        // Обробник для Hyperlink, щоб відкрити файл або папку
        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            if (e.Uri == null || string.IsNullOrWhiteSpace(e.Uri.OriginalString))
            {
                e.Handled = true;
                return;
            }

            try
            {
                string filePath = e.Uri.IsAbsoluteUri ? e.Uri.LocalPath : e.Uri.OriginalString;

                if (!string.IsNullOrEmpty(filePath) && System.IO.File.Exists(filePath))
                {
                    Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
                }
                else if (!string.IsNullOrEmpty(filePath) && System.IO.Directory.Exists(filePath))
                {
                    Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
                }
                else
                {
                    Debug.WriteLine($"[Hyperlink_RequestNavigate] Не вдалося відкрити: '{filePath}'. Файл або папка не існує.");
                    // Можна показати MessageBox, якщо потрібно
                    // MessageBox.Show($"Не вдалося відкрити: {filePath}\nФайл або папка не існує.", "Помилка відкриття", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Hyperlink_RequestNavigate] Помилка при спробі відкрити '{e.Uri?.OriginalString}': {ex.Message}");
                // MessageBox.Show($"Помилка при спробі відкрити файл: {ex.Message}", "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            e.Handled = true;
        }
    }
}