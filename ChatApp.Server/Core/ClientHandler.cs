using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using ChatApp.Server.Networking;
using ChatApp.Common.Utilities;
using System.Security.Cryptography;
using ChatApp.Common.Models;
using Newtonsoft.Json;
using System.Linq;

namespace ChatApp.Server.Core
{
    public class ClientHandler
    {
        private readonly TcpServer _server;
        private TcpClient _client;
        private string _clientId;
        private string _nickname = "UnknownUser";
        private readonly ChatDatabase _chatDatabase;

        public ClientHandler(TcpClient client, TcpServer server, ChatDatabase chatDatabase)
        {
            _client = client;
            _server = server;
            _clientId = client.Client.RemoteEndPoint?.ToString() ?? "N/A";
            _chatDatabase = chatDatabase;
            Console.WriteLine($"Server: New ClientHandler created for {_clientId}");
        }

        public TcpClient TcpClient => _client;
        public string Nickname => _nickname;

        public async Task ProcessClientAsync()
        {
            NetworkStream stream = null;
            StringBuilder receivedDataBuilder = new StringBuilder();
            byte[] buffer = new byte[8192];
            int bytesRead;

            Console.WriteLine($"Server [{_clientId}]: Starting ProcessClientAsync.");
            try
            {
                if (_client == null || !_client.Connected)
                {
                    Console.WriteLine($"Server [{_clientId}]: Client not connected at the start of ProcessClientAsync.");
                    return;
                }
                stream = _client.GetStream();
                string encryptedFirstMessage = null;
                Console.WriteLine($"Server [{_clientId}]: Waiting for the first message (nickname)...");

                while (_client.Connected && encryptedFirstMessage == null)
                {
                    if (!stream.CanRead) { Console.WriteLine($"Server [{_clientId}]: Stream no longer readable (first message)."); return; }
                    bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) { Console.WriteLine($"Server [{_clientId}]: Client disconnected (0 bytes, first message)."); return; }

                    string receivedChunk = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    receivedDataBuilder.Append(receivedChunk);
                    string currentFullBuffer = receivedDataBuilder.ToString();
                    int newlineIndex = currentFullBuffer.IndexOf('\n');
                    if (newlineIndex != -1)
                    {
                        encryptedFirstMessage = currentFullBuffer.Substring(0, newlineIndex).Trim();
                        receivedDataBuilder.Remove(0, newlineIndex + 1);
                    }
                }

                if (string.IsNullOrWhiteSpace(encryptedFirstMessage)) { Console.WriteLine($"Server [{_clientId}]: First message is empty or not received. Disconnecting."); return; }

                ChatMessage initialMessage = null;
                Console.WriteLine($"SERVER [{_clientId}]: First message (encrypted): '{encryptedFirstMessage}'");
                try
                {
                    string decryptedJson = EncryptionHelper.Decrypt(encryptedFirstMessage);
                    Console.WriteLine($"SERVER [{_clientId}]: First message (decrypted JSON): '{decryptedJson}'");
                    if (string.IsNullOrWhiteSpace(decryptedJson)) { Console.WriteLine($"SERVER [{_clientId}]: Decrypted JSON of the first message is empty. Disconnecting."); return; }

                    initialMessage = ChatMessage.FromJson(decryptedJson);

                    if (initialMessage == null) { Console.WriteLine($"Server [{_clientId}]: Failed to deserialize the first message (null). JSON: '{decryptedJson}'. Disconnecting."); return; }
                    if (initialMessage.Type != MessageType.SystemMessage || string.IsNullOrWhiteSpace(initialMessage.Sender)) { Console.WriteLine($"Server [{_clientId}]: Invalid first message (type: {initialMessage.Type}, sender: '{initialMessage.Sender}'). JSON: '{decryptedJson}'. Disconnecting."); return; }

                    if (_server.IsNicknameTaken(initialMessage.Sender))
                    {
                        Console.WriteLine($"Server [{_clientId}]: Nickname '{initialMessage.Sender}' is already taken. Disconnecting client.");
                        var errorMessage = new ChatMessage { Type = MessageType.SystemMessage, Sender = "Server", Content = "Nickname is already taken, please try another one!" };
                        string encryptedError = EncryptionHelper.Encrypt(errorMessage.ToJson());
                        byte[] errorData = Encoding.UTF8.GetBytes(encryptedError + "\n");
                        if (stream.CanWrite) await stream.WriteAsync(errorData, 0, errorData.Length);
                        await Task.Delay(50);
                        return;
                    }

                    _nickname = initialMessage.Sender;
                    Console.WriteLine($"Server [{_clientId}]: Client set nickname: {_nickname}");

                    await _server.BroadcastMessageAsync(new ChatMessage { Type = MessageType.SystemMessage, Sender = "Server", Content = $"[{_nickname}] has joined the chat." }, this);
                    await _server.SendUserListAsync();

                    List<ChatMessage> chatHistory = _chatDatabase.GetMessageHistory(50);
                    if (chatHistory.Any())
                    {
                        Console.WriteLine($"Server [{_clientId} ({_nickname})]: Sending history ({chatHistory.Count} messages).");
                        foreach (ChatMessage historyMsg in chatHistory)
                        {
                            string encryptedHistoryMessage = EncryptionHelper.Encrypt(historyMsg.ToJson());
                            byte[] historyData = Encoding.UTF8.GetBytes(encryptedHistoryMessage + "\n");
                            try
                            {
                                if (stream.CanWrite) await stream.WriteAsync(historyData, 0, historyData.Length);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Server [{_clientId} ({_nickname})]: Error sending historical message: {ex.Message}");
                                break;
                            }
                        }
                        Console.WriteLine($"Server [{_clientId} ({_nickname})]: Sending history completed.");
                    }
                    else { Console.WriteLine($"Server [{_clientId} ({_nickname})]: Chat history is empty."); }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Server [{_clientId}]: Error processing first message: {ex.GetType().Name} - {ex.Message}. Encrypted: '{encryptedFirstMessage}'. Stack: {ex.StackTrace}. Disconnecting.");
                    return;
                }

                Console.WriteLine($"Server [{_clientId} ({_nickname})]: Entering main loop. Buffer remainder: '{receivedDataBuilder.ToString().Replace("\n", "\\n").Replace("\r", "\\r")}'");
                while (_client.Connected)
                {
                    string currentProcessingBuffer = receivedDataBuilder.ToString();
                    receivedDataBuilder.Clear();

                    int nextNewlineIndex;
                    while ((nextNewlineIndex = currentProcessingBuffer.IndexOf('\n')) != -1)
                    {
                        string fullEncryptedMessage = currentProcessingBuffer.Substring(0, nextNewlineIndex).Trim();
                        currentProcessingBuffer = currentProcessingBuffer.Substring(nextNewlineIndex + 1);
                        if (string.IsNullOrWhiteSpace(fullEncryptedMessage)) continue;

                        await ProcessSingleMessageAsync(fullEncryptedMessage, stream);
                    }
                    receivedDataBuilder.Append(currentProcessingBuffer);

                    if (stream == null || !stream.CanRead)
                    {
                        Console.WriteLine($"Server [{_clientId} ({_nickname})]: Stream no longer readable (main loop).");
                        break;
                    }
                    bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                    {
                        Console.WriteLine($"Server [{_clientId} ({_nickname})]: Client disconnected (0 bytes in main loop).");
                        break;
                    }
                    string newChunk = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    receivedDataBuilder.Append(newChunk);
                }
            }
            catch (ObjectDisposedException odEx) { Console.WriteLine($"Server [{_clientId} ({_nickname})]: ObjectDisposedException in ProcessClientAsync: {odEx.Message}"); }
            catch (System.IO.IOException ioEx) { Console.WriteLine($"Server [{_clientId} ({_nickname})]: IOException in ProcessClientAsync: {ioEx.Message}."); }
            catch (Exception ex) { Console.WriteLine($"Server [{_clientId} ({_nickname})]: Unexpected error in ProcessClientAsync: {ex.Message}\n{ex.StackTrace}"); }
            finally
            {
                Console.WriteLine($"Server [{_clientId} ({_nickname})]: Finally block in ProcessClientAsync.");
                try { stream?.Close(); stream?.Dispose(); } catch { }
                try { _client?.Close(); _client?.Dispose(); } catch { }
                _server.RemoveClient(this);
                Console.WriteLine($"Server [{_clientId} ({_nickname})]: Client removed and resources closed.");
            }
        }

        private async Task ProcessSingleMessageAsync(string encryptedMessage, NetworkStream stream)
        {
            try
            {
                string decryptedJson = EncryptionHelper.Decrypt(encryptedMessage);
                ChatMessage clientMessage = ChatMessage.FromJson(decryptedJson);

                if (clientMessage == null) { Console.WriteLine($"Server [{_clientId} ({_nickname})]: Failed to deserialize message. JSON: '{decryptedJson}'."); return; }

                clientMessage.Sender = _nickname;
                clientMessage.Timestamp = DateTime.UtcNow;

                Console.WriteLine($"Server [{_clientId} ({_nickname})]: Received {clientMessage.Type} from {clientMessage.Sender}: '{clientMessage.Content?.Substring(0, Math.Min(clientMessage.Content?.Length ?? 0, 50))}'");

                switch (clientMessage.Type)
                {
                    case MessageType.ChatMessage:
                        _chatDatabase.SaveMessage(clientMessage);
                        await _server.BroadcastMessageAsync(clientMessage, this);
                        break;
                    case MessageType.Disconnect:
                        Console.WriteLine($"Server [{_clientId} ({_nickname})]: Client initiated disconnect via message.");
                        if (_client.Connected) { try { _client.Close(); } catch { } }
                        break;
                    case MessageType.FileTransferMetadata:
                        await _server.BroadcastMessageAsync(clientMessage, this);
                        break;
                    case MessageType.FileTransferChunk:
                        await _server.BroadcastMessageAsync(clientMessage, this);
                        break;
                    case MessageType.FileTransferEnd:
                        var historicFileMsg = new ChatMessage
                        {
                            Type = MessageType.HistoricFileMessage,
                            Sender = _nickname,
                            Timestamp = DateTime.UtcNow,
                            FileId = clientMessage.FileId,
                            FileName = clientMessage.FileName,
                            FileSize = clientMessage.FileSize,
                            FileMimeType = clientMessage.FileMimeType,
                            Content = $"File '{clientMessage.FileName}' was sent."
                        };
                        _chatDatabase.SaveMessage(historicFileMsg);
                        await _server.BroadcastMessageAsync(clientMessage, this);
                        break;
                    default:
                        Console.WriteLine($"Server [{_clientId} ({_nickname})]: Unknown message type {clientMessage.Type}.");
                        break;
                }
            }
            catch (Exception ex) { Console.WriteLine($"Server [{_clientId} ({_nickname})]: Error processing single message: {ex.GetType().Name} - {ex.Message}. Encrypted (partial): '{encryptedMessage.Substring(0, Math.Min(encryptedMessage.Length, 50))}'..."); }
        }

        private string TryDecrypt(string encryptedText) { try { return EncryptionHelper.Decrypt(encryptedText); } catch { return "[decryption failed]"; } }
    }
}