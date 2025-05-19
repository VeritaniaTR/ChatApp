using System;
using System.Threading.Tasks;
using ChatApp.Server.Networking;
using SQLitePCL; // Додайте цей using

namespace ChatApp.Server
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // !!! ВАЖЛИВО: Додайте цей рядок для ініціалізації SQLitePCL.raw !!!
            SQLitePCL.Batteries.Init();

            // Отримання порту з конфігурації або за замовчуванням
            int port = GetServerPort();

            TcpServer server = new TcpServer(port);

            Console.WriteLine("Запуск сервера...");
            await server.StartAsync();

            // Блокування консольного застосунку до натискання клавіші
            Console.WriteLine("Сервер працює. Натисніть будь-яку клавішу для зупинки...");
            Console.ReadKey();

            server.Stop();
        }

        static int GetServerPort()
        {
            return 12345;
        }
    }
}