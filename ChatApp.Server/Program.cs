using System;
using System.Threading.Tasks;
using ChatApp.Server.Networking;
using SQLitePCL; 

namespace ChatApp.Server
{
    class Program
    {
        static async Task Main(string[] args)
        {
            SQLitePCL.Batteries.Init();

            int port = GetServerPort();

            TcpServer server = new TcpServer(port);

            Console.WriteLine("Starting server...");
            await server.StartAsync();

            Console.WriteLine("Server is running. Press any key to stop...");
            Console.ReadKey();

            server.Stop();
        }

        static int GetServerPort()
        {
            // В реальном приложении это из конгиф файла бралось бы
            return 12345;
        }
    }
}