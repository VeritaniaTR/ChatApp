// App.xaml.cs
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
            string errorMessage = $"Сталася необроблена помилка в клієнті:\n\n" +
                                  $"Тип: {e.Exception.GetType().Name}\n" + // Додамо тип виключення
                                  $"Повідомлення: {e.Exception.Message}\n\n" +
                                  $"Стек виклику:\n{e.Exception.StackTrace}";
            if (e.Exception.InnerException != null)
            {
                errorMessage += $"\n\nВнутрішнє виключення:\nТип: {e.Exception.InnerException.GetType().Name}\n" +
                                $"Повідомлення: {e.Exception.InnerException.Message}\n\n" +
                                $"Стек виклику внутрішнього виключення:\n{e.Exception.InnerException.StackTrace}";
            }
            MessageBox.Show(errorMessage, "Критична помилка клієнта", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true; // Щоб програма не закрилася одразу, і ти встиг прочитати помилку
            // Application.Current.Shutdown(); // Якщо потрібно закрити програму після помилки
        }
    }
}