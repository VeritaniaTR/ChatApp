using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ChatApp.Common.Models;
using ChatApp.Common.Utilities;
using Newtonsoft.Json;

namespace ChatApp.Client.Services.Networking
{
    public class TcpClientService
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private string _serverIp;
        private int _serverPort;
        private string _clientNickname;

        public event Action<ChatMessage> MessageReceived;
        public event Action<bool> ConnectionStatusChanged;
        public event Action<List<string>> UserListReceived;

        public bool IsConnected => _client?.Connected ?? false;

        public async Task ConnectAsync(string serverIp, int serverPort, string nickname)
        {
            _serverIp = serverIp;
            _serverPort = serverPort;
            _clientNickname = nickname;

            if (IsConnected)
            {
                Debug.WriteLine($"Client ({_clientNickname}): Attempting to connect while already connected.");
                ConnectionStatusChanged?.Invoke(true);
                return;
            }
            if (_client != null && !_client.Connected)
            {
                Debug.WriteLine($"Client ({_clientNickname}): Previous TcpClient object exists but is not connected. Disposing old one.");
                try { _client.Dispose(); } catch { /* ignore */ }
                _client = null;
            }

            Debug.WriteLine($"Client ({_clientNickname}): Attempting to connect to {serverIp}:{serverPort}...");
            try
            {
                _client = new TcpClient();
                await _client.ConnectAsync(_serverIp, _serverPort);
                _stream = _client.GetStream();
                Debug.WriteLine($"Client ({_clientNickname}): Successfully connected to socket.");

                var connectMessage = new ChatMessage
                {
                    Type = MessageType.SystemMessage,
                    Sender = nickname,
                    Content = "Client connecting..."
                };
                string jsonConnectMessage = connectMessage.ToJson();
                Debug.WriteLine($"CLIENT ({_clientNickname}): First JSON message to send: {jsonConnectMessage}");
                string encryptedData = EncryptionHelper.Encrypt(jsonConnectMessage);
                byte[] data = Encoding.UTF8.GetBytes(encryptedData + "\n");

                await _stream.WriteAsync(data, 0, data.Length);
                await _stream.FlushAsync();
                Debug.WriteLine($"Client ({_clientNickname}): Connection message sent.");

                ConnectionStatusChanged?.Invoke(true);
                StartReceiving();
            }
            catch (SocketException ex)
            {
                Debug.WriteLine($"Client ({_clientNickname}): Socket error during connection: {ex.Message} (Code: {ex.SocketErrorCode})\n{ex.StackTrace}");
                ConnectionStatusChanged?.Invoke(false);
                await SafeDisconnectAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Client ({_clientNickname}): Unexpected error during connection: {ex.Message}\n{ex.StackTrace}");
                ConnectionStatusChanged?.Invoke(false);
                await SafeDisconnectAsync();
            }
        }

        public async Task SendMessageObjectAsync(ChatMessage messageObject)
        {
            if (!IsConnected || _stream == null)
            {
                Debug.WriteLine($"Client ({_clientNickname}): Attempting to send message '{messageObject.Type}' without an active connection.");
                return;
            }
            try
            {
                string jsonMessage = messageObject.ToJson();
                string encryptedMessage = EncryptionHelper.Encrypt(jsonMessage);
                byte[] data = Encoding.UTF8.GetBytes(encryptedMessage + "\n");
                await _stream.WriteAsync(data, 0, data.Length);
                await _stream.FlushAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Client ({_clientNickname}): Error sending message object '{messageObject.Type}': {ex.Message}");
                if (ex is System.IO.IOException || ex is SocketException)
                {
                    Debug.WriteLine($"Client ({_clientNickname}): Serious send error, initiating disconnect.");
                    await SafeDisconnectAsync();
                }
            }
        }

        public async Task SendMessageAsync(string messageContent)
        {
            var chatMessage = new ChatMessage
            {
                Type = MessageType.ChatMessage,
                Content = messageContent,
                Sender = _clientNickname
            };
            await SendMessageObjectAsync(chatMessage);
        }

        private void StartReceiving()
        {
            Debug.WriteLine($"Client ({_clientNickname}): Starting message receiving task (StartReceiving).");
            Task.Run(async () => await ReceiveAsync()).ContinueWith(t =>
            {
                if (t.IsFaulted && t.Exception != null)
                {
                    var baseException = t.Exception.GetBaseException();
                    Debug.WriteLine($"CLIENT ({_clientNickname}): CRITICAL ERROR IN RECEIVE TASK (ReceiveAsync): {baseException.Message}\nSTACK: {baseException.StackTrace}");
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        ConnectionStatusChanged?.Invoke(false);
                    });
                    // Не вызываем SafeDisconnectAsync отсюда, т.к finally в ReceiveAsync должен это делать
                }
                else if (t.IsCanceled)
                {
                    Debug.WriteLine($"CLIENT ({_clientNickname}): Receive task was cancelled.");
                }
            }, TaskContinuationOptions.ExecuteSynchronously);
        }

        private async Task ReceiveAsync()
        {
            StringBuilder receivedDataBuilder = new StringBuilder();
            byte[] buffer = new byte[8192];
            Debug.WriteLine($"Client ({_clientNickname}): ReceiveAsync task started, waiting for data...");
            try
            {
                while (_client != null && _client.Connected && _stream != null && _stream.CanRead)
                {
                    int bytesRead = 0;
                    try
                    {
                        bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
                    }
                    catch (System.IO.IOException ex)
                    {
                        Debug.WriteLine($"Client ({_clientNickname}): IOException while reading from stream in ReceiveAsync: {ex.Message}. Connection will be closed.");
                        break;
                    }
                    catch (ObjectDisposedException ex)
                    {
                        Debug.WriteLine($"Client ({_clientNickname}): ObjectDisposedException while reading from stream in ReceiveAsync: {ex.Message}. Connection will be closed.");
                        break;
                    }

                    if (bytesRead == 0)
                    {
                        Debug.WriteLine($"Client ({_clientNickname}): Server closed connection or connection lost (bytesRead == 0) in ReceiveAsync.");
                        break;
                    }

                    string receivedChunk = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    receivedDataBuilder.Append(receivedChunk);
                    string currentBufferContent = receivedDataBuilder.ToString();
                    int newlineIndex;

                    while ((newlineIndex = currentBufferContent.IndexOf('\n')) != -1)
                    {
                        string fullEncryptedMessage = currentBufferContent.Substring(0, newlineIndex).Trim();
                        receivedDataBuilder.Remove(0, newlineIndex + 1);
                        currentBufferContent = receivedDataBuilder.ToString();

                        if (string.IsNullOrWhiteSpace(fullEncryptedMessage))
                        {
                            Debug.WriteLine($"Client ({_clientNickname}): Skipped empty message after Trim.");
                            continue;
                        }

                        try
                        {
                            string decryptedJson = EncryptionHelper.Decrypt(fullEncryptedMessage);
                            ChatMessage receivedObject = ChatMessage.FromJson(decryptedJson);

                            if (receivedObject != null)
                            {
                                MessageReceived?.Invoke(receivedObject);
                                if (receivedObject.Type == MessageType.UserList)
                                {
                                    List<string> users = receivedObject.Content?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList() ?? new List<string>();
                                    UserListReceived?.Invoke(users);
                                }
                            }
                            else
                            {
                                Debug.WriteLine($"Client ({_clientNickname}): Failed to deserialize JSON to ChatMessage. JSON: '{decryptedJson.Substring(0, Math.Min(decryptedJson.Length, 100))}'...");
                            }
                        }
                        catch (Exception ex_inner_loop)
                        {
                            Debug.WriteLine($"Client ({_clientNickname}): Error processing single message: {ex_inner_loop.GetType().Name} - {ex_inner_loop.Message}. Encrypted: '{fullEncryptedMessage.Substring(0, Math.Min(fullEncryptedMessage.Length, 100))}'... Decrypted (attempt): '{TryDecrypt(fullEncryptedMessage)}'");
                        }
                    }
                }
            }
            catch (Exception ex_outer_receive_loop)
            {
                Debug.WriteLine($"Client ({_clientNickname}): Unexpected outer error in ReceiveAsync: {ex_outer_receive_loop.Message}\nSTACK: {ex_outer_receive_loop.StackTrace}");
                throw;
            }
            finally
            {
                Debug.WriteLine($"Client ({_clientNickname}): Finally block in ReceiveAsync. Current IsConnected state: {IsConnected}");
                await SafeDisconnectAsync();
                Debug.WriteLine($"Client ({_clientNickname}): ReceiveAsync task completed, SafeDisconnectAsync called.");
            }
        }

        private string TryDecrypt(string encryptedText)
        {
            try { return EncryptionHelper.Decrypt(encryptedText); }
            catch { return "[decryption failed for log]"; }
        }

        public async Task DisconnectAsync()
        {
            Debug.WriteLine($"Client ({_clientNickname}): ViewModel initiated DisconnectAsync. IsConnected: {IsConnected}");
            if (!IsConnected && _client == null)
            {
                Debug.WriteLine($"Client ({_clientNickname}): DisconnectAsync: already disconnected or was not connected.");
                ConnectionStatusChanged?.Invoke(false);
                return;
            }

            bool wasConnected = IsConnected;

            if (wasConnected && _stream != null && _stream.CanWrite)
            {
                try
                {
                    var disconnectMessage = new ChatMessage { Type = MessageType.Disconnect, Sender = _clientNickname };
                    string jsonDisconnectMessage = disconnectMessage.ToJson();
                    string encryptedDisconnectMessage = EncryptionHelper.Encrypt(jsonDisconnectMessage);
                    byte[] disconnectData = Encoding.UTF8.GetBytes(encryptedDisconnectMessage + "\n");

                    await _stream.WriteAsync(disconnectData, 0, disconnectData.Length);
                    await _stream.FlushAsync();
                    Debug.WriteLine($"Client ({_clientNickname}): Disconnect message sent to server.");
                }
                catch (Exception ex) when (ex is System.IO.IOException || ex is SocketException || ex is ObjectDisposedException)
                {
                    Debug.WriteLine($"Client ({_clientNickname}): Error (IO/Socket/Disposed) sending DisconnectMessage: {ex.Message}.");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Client ({_clientNickname}): General error sending DisconnectMessage: {ex.Message}.");
                }
            }
            await CloseClientResources();
        }

        private async Task SafeDisconnectAsync()
        {
            if (_client == null && _stream == null)
            {
                ConnectionStatusChanged?.Invoke(false);
                return;
            }
            Debug.WriteLine($"Client ({_clientNickname}): Calling SafeDisconnectAsync.");
            await CloseClientResources();
        }

        private async Task CloseClientResources()
        {
            bool resourcesWereOpen = (_stream != null || _client != null);

            NetworkStream tempStream = _stream;
            TcpClient tempClient = _client;
            _stream = null;
            _client = null;

            try { tempStream?.Close(); tempStream?.Dispose(); } catch (Exception ex) { Debug.WriteLine($"Client ({_clientNickname}): Error closing stream in CloseClientResources: {ex.Message}"); }
            try { tempClient?.Close(); tempClient?.Dispose(); } catch (Exception ex) { Debug.WriteLine($"Client ({_clientNickname}): Error closing client in CloseClientResources: {ex.Message}"); }

            if (resourcesWereOpen)
            {
                Debug.WriteLine($"Client ({_clientNickname}): Resources closed in CloseClientResources, invoking ConnectionStatusChanged(false).");
                ConnectionStatusChanged?.Invoke(false);
            }
        }

        [Obsolete("Use DisconnectAsync or SafeDisconnectAsync.")]
        public void Disconnect()
        {
            Debug.WriteLine($"Client ({_clientNickname}): Obsolete Disconnect() method called. Redirecting to DisconnectAsync.");
            Task.Run(async () => await DisconnectAsync());
        }
    }
}