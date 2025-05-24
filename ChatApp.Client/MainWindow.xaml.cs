using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using ChatApp.Client.ViewModels;

namespace ChatApp.Client.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // была ошибка, оствавил код если надо будет диагностика
     
            // if (this.DataContext == null)
            // {
            //    try
            //    {
            //        this.DataContext = new MainWindowViewModel();
            //    }
            //    catch (Exception ex)
            //    {
            //        MessageBox.Show("Error creating MainWindowViewModel in MainWindow.xaml.cs: " + ex.Message);
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
                            ChatScrollViewer.Dispatcher.InvokeAsync(() => ChatScrollViewer.ScrollToBottom());
                        }
                    };
                }
                else
                {
                    Debug.WriteLine("[MainWindow.Loaded] DataContext is not MainWindowViewModel or is null.");
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

        private void MessageTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (this.DataContext is MainWindowViewModel viewModel)
                {
                    if (viewModel.SendCommand.CanExecute(null))
                    {
                        viewModel.SendCommand.Execute(null);
                    }
                }
            }
        }

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
                    Debug.WriteLine($"[Hyperlink_RequestNavigate] Failed to open: '{filePath}'. File or directory does not exist.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Hyperlink_RequestNavigate] Error trying to open '{e.Uri?.OriginalString}': {ex.Message}");
            }
            e.Handled = true;
        }
    }
}