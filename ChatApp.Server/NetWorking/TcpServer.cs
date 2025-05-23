// Початок файлу TcpServer.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using ChatApp.Server.Core;
using ChatApp.Common.Utilities;
using ChatApp.Common.Models;

namespace ChatApp.Server.Networking
{
    public class TcpServer
    {
        private TcpListener _listener;
        private readonly int _port;
        private readonly List<ClientHandler> _connectedClients = new List<ClientHandler>();
        private readonly ChatDatabase _chatDatabase;
        private readonly object _clientsLock = new object();

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
                    ClientHandler clientHandler = new ClientHandler(client, this, _chatDatabase);

                    lock (_clientsLock)
                    {
                        _connectedClients.Add(clientHandler);
                    }
                    Console.WriteLine($"Сервер: Клієнт підключився: {client.Client.RemoteEndPoint}. Всього клієнтів: {_connectedClients.Count}");

                    _ = Task.Run(async () => await clientHandler.ProcessClientAsync());
                }
            }
            catch (SocketException ex) { Console.WriteLine($"Сервер: Помилка сокету TcpListener: {ex.Message}"); }
            catch (Exception ex) { Console.WriteLine($"Сервер: Неочікувана помилка TcpListener: {ex.Message}\n{ex.StackTrace}"); }
            finally { Stop(); }
        }

        public void Stop()
        {
            _listener?.Stop();
            Console.WriteLine("Сервер: Зупинка сервера...");
            List<ClientHandler> clientsToClose;
            lock (_clientsLock)
            {
                clientsToClose = _connectedClients.ToList();
                _connectedClients.Clear();
            }

            foreach (var clientHandler in clientsToClose)
            {
                try
                {
                    // Можна надіслати повідомлення про закриття сервера, але клієнти, ймовірно, отримають IOException
                    clientHandler.TcpClient?.Close();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Сервер: Помилка закриття клієнта {clientHandler.Nickname}: {ex.Message}");
                }
            }
            Console.WriteLine("Сервер зупинено.");
        }

        public void RemoveClient(ClientHandler clientHandler)
        {
            if (clientHandler == null) return;

            bool removed;
            lock (_clientsLock)
            {
                removed = _connectedClients.Remove(clientHandler);
            }

            if (removed)
            {
                string nickname = clientHandler.Nickname ?? "Невідомий";
                string endpoint = clientHandler.TcpClient?.Client?.RemoteEndPoint?.ToString() ?? "N/A";
                Console.WriteLine($"Сервер: Клієнта {nickname} ({endpoint}) видалено зі списку. Залишилося клієнтів: {_connectedClients.Count}");

                if (nickname != "UnknownUser" && !string.IsNullOrWhiteSpace(nickname))
                {
                    var disconnectNotification = new ChatMessage
                    {
                        Type = MessageType.SystemMessage,
                        Sender = "Server",
                        Content = $"[{nickname}] покинув чат."
                    };
                    // Транслюємо всім, хто залишився (senderToExclude = null, оскільки clientHandler вже видалений з _connectedClients (якщо не було помилок))
                    // Або можна передати clientHandler, щоб точно його виключити, якщо він ще там є з якоїсь причини
                    _ = Task.Run(async () => await BroadcastMessageAsync(disconnectNotification, null));
                }
                _ = Task.Run(SendUserListAsync);
            }
        }

        public async Task BroadcastMessageAsync(ChatMessage chatMessage, ClientHandler senderToExclude = null)
        {
            if (string.IsNullOrEmpty(chatMessage.Sender))
            {
                chatMessage.Sender = "Server";
            }
            chatMessage.Timestamp = DateTime.UtcNow;

            string jsonMessage = chatMessage.ToJson();
            if (jsonMessage == null)
            {
                Console.WriteLine($"Сервер: Помилка серіалізації повідомлення типу {chatMessage.Type} від {chatMessage.Sender} в JSON (null). Трансляція скасована.");
                return;
            }
            string encryptedMessage = EncryptionHelper.Encrypt(jsonMessage);
            byte[] data = Encoding.UTF8.GetBytes(encryptedMessage + "\n");

            List<ClientHandler> currentClientsSnapshot;
            lock (_clientsLock)
            {
                currentClientsSnapshot = _connectedClients.ToList();
            }

            //Console.WriteLine($"Сервер: Трансляція повідомлення типу {chatMessage.Type} від {chatMessage.Sender} для {currentClientsSnapshot.Count} клієнтів (виключаючи: {senderToExclude?.Nickname})");

            foreach (var clientHandler in currentClientsSnapshot)
            {
                if (clientHandler.TcpClient.Connected && clientHandler != senderToExclude)
                {
                    try
                    {
                        NetworkStream stream = clientHandler.TcpClient.GetStream();
                        if (stream.CanWrite)
                        {
                            await stream.WriteAsync(data, 0, data.Length);
                            await stream.FlushAsync();
                        }
                    }
                    catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException)
                    {
                        Console.WriteLine($"Сервер: Помилка IO/Disposed при надсиланні повідомлення клієнту {clientHandler.Nickname} ({clientHandler.TcpClient?.Client?.RemoteEndPoint}): {ex.Message}. Можливо, клієнт відключився.");
                        // Розглянути можливість видалення цього клієнта тут, але обережно з модифікацією колекції під час ітерації
                        // Можна створити список для видалення і видалити їх після циклу
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Сервер: Загальна помилка надсилання повідомлення клієнту {clientHandler.Nickname} ({clientHandler.TcpClient?.Client?.RemoteEndPoint}): {ex.Message}");
                    }
                }
            }
        }

        public async Task SendUserListAsync()
        {
            List<string> userNames;
            lock (_clientsLock)
            {
                userNames = _connectedClients
                    .Where(c => c.TcpClient.Connected && !string.IsNullOrWhiteSpace(c.Nickname) && c.Nickname != "UnknownUser")
                    .Select(c => c.Nickname)
                    .Distinct()
                    .ToList();
            }

            Console.WriteLine($"Сервер: Надсилання списку користувачів: [{string.Join(", ", userNames)}] для {_connectedClients.Count(c => c.TcpClient.Connected)} активних клієнтів.");
            string userListJson = string.Join(",", userNames);
            var userListMessage = new ChatMessage
            {
                Type = MessageType.UserList,
                Sender = "Server",
                Content = userListJson,
                Timestamp = DateTime.UtcNow
            };

            await BroadcastMessageAsync(userListMessage, null);
        }

        public bool IsNicknameTaken(string nickname)
        {
            lock (_clientsLock)
            {
                return _connectedClients.Any(c => c.TcpClient.Connected && c.Nickname.Equals(nickname, StringComparison.OrdinalIgnoreCase));
            }
        }
    }
}