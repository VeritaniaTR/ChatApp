// Початок файлу ClientHandler.cs
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
            Console.WriteLine($"Сервер: Створено новий ClientHandler для {_clientId}");
        }

        public TcpClient TcpClient => _client;
        public string Nickname => _nickname;

        public async Task ProcessClientAsync()
        {
            NetworkStream stream = null;
            StringBuilder receivedDataBuilder = new StringBuilder();
            byte[] buffer = new byte[8192];
            int bytesRead;

            Console.WriteLine($"Сервер [{_clientId}]: Початок ProcessClientAsync.");
            try
            {
                if (_client == null || !_client.Connected)
                {
                    Console.WriteLine($"Сервер [{_clientId}]: Клієнт не підключений на початку ProcessClientAsync.");
                    return;
                }
                stream = _client.GetStream();
                string encryptedFirstMessage = null;
                Console.WriteLine($"Сервер [{_clientId}]: Очікування першого повідомлення (нікнейм)...");

                while (_client.Connected && encryptedFirstMessage == null)
                {
                    if (!stream.CanRead) { Console.WriteLine($"Сервер [{_clientId}]: Потік більше не доступний для читання (перше повідомлення)."); return; }
                    bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) { Console.WriteLine($"Сервер [{_clientId}]: Клієнт відключився (0 байт, перше повідомлення)."); return; }

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

                if (string.IsNullOrWhiteSpace(encryptedFirstMessage)) { Console.WriteLine($"Сервер [{_clientId}]: Перше повідомлення порожнє або не отримано. Відключення."); return; }

                ChatMessage initialMessage = null;
                Console.WriteLine($"СЕРВЕР [{_clientId}]: Перше повідомлення (зашифроване): '{encryptedFirstMessage}'");
                try
                {
                    string decryptedJson = EncryptionHelper.Decrypt(encryptedFirstMessage);
                    Console.WriteLine($"СЕРВЕР [{_clientId}]: Перше повідомлення (розшифроване JSON): '{decryptedJson}'");
                    if (string.IsNullOrWhiteSpace(decryptedJson)) { Console.WriteLine($"СЕРВЕР [{_clientId}]: Розшифрований JSON першого повідомлення порожній. Відключення."); return; }

                    initialMessage = ChatMessage.FromJson(decryptedJson);

                    if (initialMessage == null) { Console.WriteLine($"Сервер [{_clientId}]: Не вдалося десеріалізувати перше повідомлення (null). JSON: '{decryptedJson}'. Відключення."); return; }
                    if (initialMessage.Type != MessageType.SystemMessage || string.IsNullOrWhiteSpace(initialMessage.Sender)) { Console.WriteLine($"Сервер [{_clientId}]: Недійсне перше повідомлення (тип: {initialMessage.Type}, sender: '{initialMessage.Sender}'). JSON: '{decryptedJson}'. Відключення."); return; }

                    if (_server.IsNicknameTaken(initialMessage.Sender))
                    {
                        Console.WriteLine($"Сервер [{_clientId}]: Нікнейм '{initialMessage.Sender}' вже зайнятий. Відключення.");
                        var errorMessage = new ChatMessage { Type = MessageType.SystemMessage, Sender = "Server", Content = "Нікнейм вже зайнятий, спробуйте інший!" };
                        // ... (код надсилання errorMessage)
                        string encryptedError = EncryptionHelper.Encrypt(errorMessage.ToJson());
                        byte[] errorData = Encoding.UTF8.GetBytes(encryptedError + "\n");
                        if (stream.CanWrite) await stream.WriteAsync(errorData, 0, errorData.Length);
                        await Task.Delay(50);
                        return;
                    }

                    _nickname = initialMessage.Sender;
                    Console.WriteLine($"Сервер [{_clientId}]: Клієнт встановив нікнейм: {_nickname}");

                    await _server.BroadcastMessageAsync(new ChatMessage { Type = MessageType.SystemMessage, Sender = "Server", Content = $"[{_nickname}] приєднався до чату." }, this);
                    await _server.SendUserListAsync();

                    // Надсилання історії чату
                    List<ChatMessage> chatHistory = _chatDatabase.GetMessageHistory(50);
                    if (chatHistory.Any())
                    {
                        Console.WriteLine($"Сервер [{_clientId} ({_nickname})]: Надсилання історії ({chatHistory.Count} повідомлень).");
                        foreach (ChatMessage historyMsg in chatHistory)
                        {
                            // Переконуємося, що історичні повідомлення мають правильний тип для відображення на клієнті
                            // Якщо в БД вони зберігаються як ChatMessage, то все ок.
                            // Якщо як SystemMessage, клієнт має їх коректно обробити.
                            // Для послідовності, можна припустити, що всі історичні повідомлення - це ChatMessage
                            // або спеціальний HistoricChatMessage. Поки що, використовуємо те, що є в historyMsg.Type
                            string encryptedHistoryMessage = EncryptionHelper.Encrypt(historyMsg.ToJson());
                            byte[] historyData = Encoding.UTF8.GetBytes(encryptedHistoryMessage + "\n");
                            try
                            {
                                if (stream.CanWrite) await stream.WriteAsync(historyData, 0, historyData.Length);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Сервер [{_clientId} ({_nickname})]: Помилка надсилання історичного повідомлення: {ex.Message}");
                                break;
                            }
                        }
                        Console.WriteLine($"Сервер [{_clientId} ({_nickname})]: Надсилання історії завершено.");
                    }
                    else { Console.WriteLine($"Сервер [{_clientId} ({_nickname})]: Історія чату порожня."); }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Сервер [{_clientId}]: Помилка обробки першого повідомлення: {ex.GetType().Name} - {ex.Message}. Зашифроване: '{encryptedFirstMessage}'. Стек: {ex.StackTrace}. Відключення.");
                    return;
                }

                Console.WriteLine($"Сервер [{_clientId} ({_nickname})]: Перехід до основного циклу. Залишок у буфері: '{receivedDataBuilder.ToString().Replace("\n", "\\n").Replace("\r", "\\r")}'");
                while (_client.Connected) // Основний цикл читання
                {
                    string currentProcessingBuffer = receivedDataBuilder.ToString();
                    receivedDataBuilder.Clear();

                    int nextNewlineIndex;
                    while ((nextNewlineIndex = currentProcessingBuffer.IndexOf('\n')) != -1)
                    {
                        string fullEncryptedMessage = currentProcessingBuffer.Substring(0, nextNewlineIndex).Trim();
                        currentProcessingBuffer = currentProcessingBuffer.Substring(nextNewlineIndex + 1);
                        if (string.IsNullOrWhiteSpace(fullEncryptedMessage)) continue;

                        // Обробка отриманого повідомлення
                        await ProcessSingleMessageAsync(fullEncryptedMessage, stream);
                    }
                    receivedDataBuilder.Append(currentProcessingBuffer); // Повертаємо залишок без \n

                    if (stream == null || !stream.CanRead)
                    {
                        Console.WriteLine($"Сервер [{_clientId} ({_nickname})]: Потік більше не доступний для читання (основний цикл).");
                        break;
                    }
                    bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                    {
                        Console.WriteLine($"Сервер [{_clientId} ({_nickname})]: Клієнт відключився (0 байт в основному циклі).");
                        break;
                    }
                    string newChunk = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    receivedDataBuilder.Append(newChunk);
                }
            }
            catch (ObjectDisposedException odEx) { Console.WriteLine($"Сервер [{_clientId} ({_nickname})]: ObjectDisposedException в ProcessClientAsync: {odEx.Message}"); }
            catch (System.IO.IOException ioEx) { Console.WriteLine($"Сервер [{_clientId} ({_nickname})]: IOException в ProcessClientAsync: {ioEx.Message}."); }
            catch (Exception ex) { Console.WriteLine($"Сервер [{_clientId} ({_nickname})]: Неочікувана помилка в ProcessClientAsync: {ex.Message}\n{ex.StackTrace}"); }
            finally
            {
                Console.WriteLine($"Сервер [{_clientId} ({_nickname})]: Блок finally в ProcessClientAsync.");
                try { stream?.Close(); stream?.Dispose(); } catch { }
                try { _client?.Close(); _client?.Dispose(); } catch { }
                _server.RemoveClient(this); // Це має викликати BroadcastMessageAsync про відключення та SendUserListAsync
                Console.WriteLine($"Сервер [{_clientId} ({_nickname})]: Клієнта видалено та ресурси закрито.");
            }
        }

        private async Task ProcessSingleMessageAsync(string encryptedMessage, NetworkStream stream)
        {
            try
            {
                string decryptedJson = EncryptionHelper.Decrypt(encryptedMessage);
                ChatMessage clientMessage = ChatMessage.FromJson(decryptedJson);

                if (clientMessage == null) { Console.WriteLine($"Сервер [{_clientId} ({_nickname})]: Не вдалося десеріалізувати повідомлення. JSON: '{decryptedJson}'."); return; }

                clientMessage.Sender = _nickname;
                clientMessage.Timestamp = DateTime.UtcNow;

                Console.WriteLine($"Сервер [{_clientId} ({_nickname})]: Отримано {clientMessage.Type} від {clientMessage.Sender}: '{clientMessage.Content?.Substring(0, Math.Min(clientMessage.Content?.Length ?? 0, 50))}'");

                switch (clientMessage.Type)
                {
                    case MessageType.ChatMessage:
                        _chatDatabase.SaveMessage(clientMessage); // Зберігаємо повний ChatMessage
                        await _server.BroadcastMessageAsync(clientMessage, this);
                        break;
                    case MessageType.Disconnect:
                        Console.WriteLine($"Сервер [{_clientId} ({_nickname})]: Клієнт ініціював відключення через повідомлення.");
                        if (_client.Connected) { try { _client.Close(); } catch { } }
                        break;
                    case MessageType.FileTransferMetadata:
                        // Можливо, тут варто залогувати метадані, але поки просто транслюємо
                        await _server.BroadcastMessageAsync(clientMessage, this);
                        break;
                    case MessageType.FileTransferChunk:
                        await _server.BroadcastMessageAsync(clientMessage, this);
                        break;
                    case MessageType.FileTransferEnd:
                        // Зберігаємо метадані файлу в історію, коли передача завершена
                        var historicFileMsg = new ChatMessage
                        {
                            Type = MessageType.HistoricFileMessage,
                            Sender = _nickname,
                            Timestamp = DateTime.UtcNow,
                            FileId = clientMessage.FileId,
                            FileName = clientMessage.FileName,
                            FileSize = clientMessage.FileSize,
                            FileMimeType = clientMessage.FileMimeType,
                            Content = $"Файл '{clientMessage.FileName}' було надіслано."
                        };
                        _chatDatabase.SaveMessage(historicFileMsg);
                        // Транслюємо оригінальне повідомлення FileTransferEnd
                        await _server.BroadcastMessageAsync(clientMessage, this);
                        break;
                    default:
                        Console.WriteLine($"Сервер [{_clientId} ({_nickname})]: Невідомий тип повідомлення {clientMessage.Type}.");
                        break;
                }
            }
            catch (Exception ex) { Console.WriteLine($"Сервер [{_clientId} ({_nickname})]: Помилка обробки одного повідомлення: {ex.GetType().Name} - {ex.Message}. Зашифроване (частково): '{encryptedMessage.Substring(0, Math.Min(encryptedMessage.Length, 50))}'..."); }
        }

        private string TryDecrypt(string encryptedText) { try { return EncryptionHelper.Decrypt(encryptedText); } catch { return "[не вдалося розшифрувати]"; } }
    }
}