using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using ChatApp.Server.Core;
using ChatApp.Common.Utilities;
using ChatApp.Common.Models; // Додано для ChatMessage

namespace ChatApp.Server.Networking
{
    public class TcpServer
    {
        private TcpListener _listener;
        private int _port;
        private List<ClientHandler> _connectedClients = new List<ClientHandler>();
        private ChatDatabase _chatDatabase;

        public TcpServer(int port)
        {
            _port = port;
            _chatDatabase = new ChatDatabase();
        }

        public async Task StartAsync()
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, _port);
                _listener.Start();
                Console.WriteLine($"Сервер запущено на порту {_port}. Очікування підключень...");

                while (true)
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync();
                    Console.WriteLine($"Сервер: Клієнт підключився: {client.Client.RemoteEndPoint}");

                    ClientHandler clientHandler = new ClientHandler(client, this, _chatDatabase);
                    _connectedClients.Add(clientHandler);

                    Task.Run(async () =>
                    {
                        await clientHandler.ProcessClientAsync();
                    });
                }
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"Сервер: Помилка сокету: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Сервер: Неочікувана помилка: {ex.Message}");
            }
            finally
            {
                Stop();
            }
        }

        public void Stop()
        {
            _listener?.Stop();
            Console.WriteLine("Сервер зупинено.");
        }

        public void RemoveClient(ClientHandler clientHandler)
        {
            _connectedClients.Remove(clientHandler);
            Console.WriteLine($"Сервер: Клієнта {clientHandler.Nickname} видалено зі списку: {clientHandler.TcpClient.Client.RemoteEndPoint}");
            Task.Run(SendUserListAsync); // Оновлюємо список після видалення клієнта
        }

        public async Task BroadcastMessageAsync(ChatMessage chatMessage, ClientHandler sender = null)
        {
            // Якщо Sender не встановлено, це системне повідомлення від сервера
            if (string.IsNullOrEmpty(chatMessage.Sender))
            {
                chatMessage.Sender = "Server";
            }
            chatMessage.Timestamp = DateTime.Now; // Гарантуємо актуальний timestamp на момент розсилки

            string jsonMessage = chatMessage.ToJson(); // Серіалізуємо ChatMessage в JSON
            string encryptedMessage = EncryptionHelper.Encrypt(jsonMessage);
            byte[] data = Encoding.UTF8.GetBytes(encryptedMessage + "\n");

            foreach (var clientHandler in _connectedClients.ToList())
            {
                // НЕ надсилаємо повідомлення назад відправнику
                // Якщо sender не null, і це саме той відправник, пропускаємо.
                if (clientHandler.TcpClient.Connected && (sender == null || clientHandler != sender))
                {
                    try
                    {
                        await clientHandler.TcpClient.GetStream().WriteAsync(data, 0, data.Length);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Сервер: Помилка надсилання повідомлення клієнту {clientHandler.Nickname} ({clientHandler.TcpClient.Client.RemoteEndPoint}): {ex.Message}");
                    }
                }
            }
        }

        public async Task SendUserListAsync()
        {
            List<string> userNames = new List<string>();
            userNames = _connectedClients.Where(c => c.TcpClient.Connected && !string.IsNullOrWhiteSpace(c.Nickname) && c.Nickname != "UnknownUser")
                                         .Select(c => c.Nickname)
                                         .Distinct()
                                         .ToList();

            string userListJson = string.Join(",", userNames);
            // Створюємо ChatMessage для списку користувачів
            var userListMessage = new ChatMessage
            {
                Type = MessageType.UserList,
                Sender = "Server",
                Content = userListJson
            };
            string jsonUserListMessage = userListMessage.ToJson();

            string encryptedMessage = EncryptionHelper.Encrypt(jsonUserListMessage);
            byte[] data = Encoding.UTF8.GetBytes(encryptedMessage + "\n");

            foreach (var clientHandler in _connectedClients.ToList())
            {
                if (clientHandler.TcpClient.Connected)
                {
                    try
                    {
                        await clientHandler.TcpClient.GetStream().WriteAsync(data, 0, data.Length);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Сервер: Помилка надсилання списку користувачів клієнту {clientHandler.Nickname} ({clientHandler.TcpClient.Client.RemoteEndPoint}): {ex.Message}");
                    }
                }
            }
        }

        public bool IsNicknameTaken(string nickname)
        {
            return _connectedClients.Any(c => c.TcpClient.Connected && c.Nickname.Equals(nickname, StringComparison.OrdinalIgnoreCase));
        }
    }
}