using System.Windows;
using System.Windows.Threading;

namespace ChatApp.Client
{
    public partial class App : Application
    {
        public App()
        {
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
        }

        void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            string errorMessage = $"An unhandled exception occurred in the client:\n\n" +
                                  $"Type: {e.Exception.GetType().Name}\n" +
                                  $"Message: {e.Exception.Message}\n\n" +
                                  $"Stack Trace:\n{e.Exception.StackTrace}";
            if (e.Exception.InnerException != null)
            {
                errorMessage += $"\n\nInner Exception:\nType: {e.Exception.InnerException.GetType().Name}\n" +
                                $"Message: {e.Exception.InnerException.Message}\n\n" +
                                $"Inner Exception Stack Trace:\n{e.Exception.InnerException.StackTrace}";
            }
            MessageBox.Show(errorMessage, "Client Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true; // Щоб програма не закрилася сразу
            // Application.Current.Shutdown(); // если надо будет закрыть потом
        }
    }
}