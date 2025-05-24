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
                Console.WriteLine($"Server started on port {_port}. Waiting for connections...");

                while (true)
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync();
                    ClientHandler clientHandler = new ClientHandler(client, this, _chatDatabase);

                    lock (_clientsLock)
                    {
                        _connectedClients.Add(clientHandler);
                    }
                    Console.WriteLine($"Server: Client connected: {client.Client.RemoteEndPoint}. Total clients: {_connectedClients.Count}");

                    _ = Task.Run(async () => await clientHandler.ProcessClientAsync());
                }
            }
            catch (SocketException ex) { Console.WriteLine($"Server: TcpListener socket error: {ex.Message}"); }
            catch (Exception ex) { Console.WriteLine($"Server: Unexpected TcpListener error: {ex.Message}\n{ex.StackTrace}"); }
            finally { Stop(); }
        }

        public void Stop()
        {
            _listener?.Stop();
            Console.WriteLine("Server: Stopping server...");
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
                    // It's possible to send a server shutdown message, but clients will likely get an IOException
                    clientHandler.TcpClient?.Close();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Server: Error closing client {clientHandler.Nickname}: {ex.Message}");
                }
            }
            Console.WriteLine("Server stopped.");
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
                string nickname = clientHandler.Nickname ?? "Unknown";
                string endpoint = clientHandler.TcpClient?.Client?.RemoteEndPoint?.ToString() ?? "N/A";
                Console.WriteLine($"Server: Client {nickname} ({endpoint}) removed from list. Clients remaining: {_connectedClients.Count}");

                if (nickname != "UnknownUser" && !string.IsNullOrWhiteSpace(nickname))
                {
                    var disconnectNotification = new ChatMessage
                    {
                        Type = MessageType.SystemMessage,
                        Sender = "Server",
                        Content = $"[{nickname}] has left the chat."
                    };
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
                Console.WriteLine($"Server: Error serializing message type {chatMessage.Type} from {chatMessage.Sender} to JSON (null). Broadcast cancelled.");
                return;
            }
            string encryptedMessage = EncryptionHelper.Encrypt(jsonMessage);
            byte[] data = Encoding.UTF8.GetBytes(encryptedMessage + "\n");

            List<ClientHandler> currentClientsSnapshot;
            lock (_clientsLock)
            {
                currentClientsSnapshot = _connectedClients.ToList();
            }

            // Console.WriteLine($"Server: Broadcasting message type {chatMessage.Type} from {chatMessage.Sender} to {currentClientsSnapshot.Count} clients (excluding: {senderToExclude?.Nickname})");

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
                        Console.WriteLine($"Server: IO/Disposed error sending message to client {clientHandler.Nickname} ({clientHandler.TcpClient?.Client?.RemoteEndPoint}): {ex.Message}. Client may have disconnected.");
                        // Consider removing this client here, but be careful with modifying the collection during iteration.
                        // Could create a list for removal and remove them after the loop.
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Server: General error sending message to client {clientHandler.Nickname} ({clientHandler.TcpClient?.Client?.RemoteEndPoint}): {ex.Message}");
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

            Console.WriteLine($"Server: Sending user list: [{string.Join(", ", userNames)}] for {_connectedClients.Count(c => c.TcpClient.Connected)} active clients.");
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