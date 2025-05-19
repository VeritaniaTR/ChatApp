using System.Windows;
using System.Windows.Threading; // Потрібно для DispatcherUnhandledExceptionEventArgs

namespace ChatApp.Client
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        // Конструктор App
        public App()
        {
            // Підписуємося на подію необроблених виключень Dispatcher
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
        }

        void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // Логуємо помилку або показуємо її користувачеві
            // Це допоможе зрозуміти, чому клієнт "падає"
            string errorMessage = $"Сталася необроблена помилка в клієнті:\n\n" +
                                  $"Повідомлення: {e.Exception.Message}\n\n" +
                                  $"Стек виклику:\n{e.Exception.StackTrace}";

            // Можна використовувати System.Diagnostics.Debug.WriteLine(errorMessage); для виводу в Output вікно Visual Studio
            // Або показати MessageBox
            MessageBox.Show(errorMessage, "Критична помилка клієнта", MessageBoxButton.OK, MessageBoxImage.Error);

            // Позначити помилку як оброблену, щоб стандартний обробник .NET не завершив програму аварійно.
            // Якщо ви хочете, щоб програма все одно закрилася після показу помилки,
            // можна закоментувати e.Handled = true; або викликати Application.Current.Shutdown();
            e.Handled = true;

            // За бажанням, можна примусово закрити програму після критичної помилки
            // Application.Current.Shutdown();
        }
    }
}