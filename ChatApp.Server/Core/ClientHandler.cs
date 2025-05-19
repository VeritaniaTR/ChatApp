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
            _clientId = client.Client.RemoteEndPoint.ToString();
            _chatDatabase = chatDatabase;
        }

        public TcpClient TcpClient => _client;
        public string Nickname => _nickname;

        public async Task ProcessClientAsync()
        {
            NetworkStream stream = _client.GetStream();
            byte[] buffer = new byte[4096];
            StringBuilder receivedDataBuilder = new StringBuilder();
            int bytesRead;

            try
            {
                string encryptedFirstMessage = null;
                while (_client.Connected && encryptedFirstMessage == null)
                {
                    bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                    {
                        Console.WriteLine($"Сервер: Клієнт {_client.Client.RemoteEndPoint} ({_nickname}) відключився до надсилання першого повідомлення.");
                        return;
                    }
                    receivedDataBuilder.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
                    int newlineIndex = receivedDataBuilder.ToString().IndexOf('\n');
                    if (newlineIndex != -1)
                    {
                        encryptedFirstMessage = receivedDataBuilder.ToString().Substring(0, newlineIndex);
                        receivedDataBuilder.Remove(0, newlineIndex + 1);
                    }
                }

                ChatMessage initialMessage = null;
                if (encryptedFirstMessage != null)
                {
                    // === ЗМІНА: Додано логування ===
                    Console.WriteLine($"СЕРВЕР: Перше повідомлення (зашифроване) від {_client.Client.RemoteEndPoint}: '{encryptedFirstMessage}'");
                    // ===============================
                    try
                    {
                        string decryptedJson = EncryptionHelper.Decrypt(encryptedFirstMessage);
                        // === ЗМІНА: Додано логування ===
                        Console.WriteLine($"СЕРВЕР: Перше повідомлення (розшифроване JSON) від {_client.Client.RemoteEndPoint}: '{decryptedJson}'");
                        // ===============================
                        initialMessage = ChatMessage.FromJson(decryptedJson); // Тут виникала помилка

                        if (initialMessage == null || initialMessage.Type != MessageType.SystemMessage || string.IsNullOrWhiteSpace(initialMessage.Sender))
                        {
                            Console.WriteLine($"Сервер: Клієнт {_client.Client.RemoteEndPoint} ({_nickname}) надіслав недійсне перше повідомлення або відсутній нікнейм. Вміст: '{decryptedJson}'. Відключення.");
                            return;
                        }

                        if (_server.IsNicknameTaken(initialMessage.Sender))
                        {
                            Console.WriteLine($"Сервер: Нікнейм '{initialMessage.Sender}' вже зайнятий. Відключення клієнта {_client.Client.RemoteEndPoint}.");
                            var errorMessage = new ChatMessage
                            {
                                Type = MessageType.SystemMessage,
                                Sender = "Server",
                                Content = "Нікнейм вже зайнятий, спробуйте інший!"
                            };
                            string encryptedError = EncryptionHelper.Encrypt(errorMessage.ToJson());
                            byte[] errorData = Encoding.UTF8.GetBytes(encryptedError + "\n");
                            await stream.WriteAsync(errorData, 0, errorData.Length);
                            await Task.Delay(50);
                            return;
                        }

                        _nickname = initialMessage.Sender;
                        Console.WriteLine($"Сервер: Клієнт {_client.Client.RemoteEndPoint} встановив нікнейм: {_nickname}");

                        await _server.BroadcastMessageAsync(new ChatMessage { Type = MessageType.SystemMessage, Sender = "Server", Content = $"[{_nickname}] приєднався до чату." }, this);
                        await _server.SendUserListAsync();

                        // ... (код надсилання історії - без змін) ...
                        List<string> chatHistory = _chatDatabase.GetMessageHistory(50);
                        if (chatHistory.Any())
                        {
                            Console.WriteLine($"Сервер: Надсилання історії ({chatHistory.Count} повідомлень) клієнту {_nickname}.");
                            foreach (var historyMessageContent in chatHistory)
                            {
                                var serverFormattedHistoryMessage = new ChatMessage
                                {
                                    Type = MessageType.SystemMessage,
                                    Sender = "Server_History",
                                    Content = historyMessageContent
                                };

                                string encryptedHistoryMessage = EncryptionHelper.Encrypt(serverFormattedHistoryMessage.ToJson());
                                byte[] historyData = Encoding.UTF8.GetBytes(encryptedHistoryMessage + "\n");
                                try
                                {
                                    await stream.WriteAsync(historyData, 0, historyData.Length);
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Сервер: Помилка надсилання історії клієнту {_nickname}: {ex.Message}");
                                    break;
                                }
                            }
                        }
                    }
                    catch (JsonSerializationException ex)
                    {
                        Console.WriteLine($"Сервер: Помилка десеріалізації JSON першого повідомлення від {_client.Client.RemoteEndPoint} ({_nickname}): {ex.Message}. Зашифроване: '{encryptedFirstMessage}'. Розшифроване (спроба): '{TryDecrypt(encryptedFirstMessage)}'. Відключення.");
                        return;
                    }
                    catch (FormatException ex)
                    {
                        Console.WriteLine($"Сервер: Помилка формату (Base64) першого повідомлення від {_client.Client.RemoteEndPoint} ({_nickname}): {ex.Message}. Зашифроване: '{encryptedFirstMessage}'. Відключення.");
                        return;
                    }
                    catch (CryptographicException ex)
                    {
                        Console.WriteLine($"Сервер: Помилка дешифрування першого повідомлення від {_client.Client.RemoteEndPoint} ({_nickname}): {ex.Message}. Зашифроване: '{encryptedFirstMessage}'. Відключення.");
                        return;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Сервер: Неочікувана помилка при отриманні першого повідомлення від {_client.Client.RemoteEndPoint} ({_nickname}): {ex.Message}. Зашифроване: '{encryptedFirstMessage}'. Відключення.");
                        return;
                    }
                }
                else
                {
                    Console.WriteLine($"Сервер: Клієнт {_client.Client.RemoteEndPoint} ({_nickname}) не надіслав перше повідомлення (encryptedFirstMessage is null). Відключення.");
                    return;
                }

                // ... (Основний цикл читання повідомлень - без змін відносно попередньої версії) ...
                while (_client.Connected && (bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    receivedDataBuilder.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
                    string currentBuffer = receivedDataBuilder.ToString();
                    int newlineIndex;

                    while ((newlineIndex = currentBuffer.IndexOf('\n')) != -1)
                    {
                        string fullEncryptedMessage = currentBuffer.Substring(0, newlineIndex);
                        receivedDataBuilder.Remove(0, newlineIndex + 1);
                        currentBuffer = receivedDataBuilder.ToString();

                        if (string.IsNullOrWhiteSpace(fullEncryptedMessage)) continue;

                        try
                        {
                            string decryptedJson = EncryptionHelper.Decrypt(fullEncryptedMessage).Trim();
                            ChatMessage receivedClientMessage = ChatMessage.FromJson(decryptedJson);

                            if (receivedClientMessage == null)
                            {
                                Console.WriteLine($"Сервер: Отримано недійсний JSON від {_nickname} (після десеріалізації null). Пропускаємо.");
                                continue;
                            }

                            //Console.WriteLine($"Сервер: Отримано від {_nickname} (Тип: {receivedClientMessage.Type}): {receivedClientMessage.Content}");

                            switch (receivedClientMessage.Type)
                            {
                                case MessageType.ChatMessage:
                                    var chatMsgToBroadcast = new ChatMessage
                                    {
                                        Type = MessageType.ChatMessage,
                                        Sender = _nickname,
                                        Content = receivedClientMessage.Content,
                                        Timestamp = DateTime.Now
                                    };
                                    _chatDatabase.SaveMessage(chatMsgToBroadcast.Timestamp, chatMsgToBroadcast.Sender, chatMsgToBroadcast.Content);
                                    await _server.BroadcastMessageAsync(chatMsgToBroadcast, this);
                                    break;
                                case MessageType.Disconnect:
                                    Console.WriteLine($"Сервер: Клієнт {_nickname} ініціював відключення.");
                                    return;
                                default:
                                    // Транслюємо FileTransfer типи без змін, але з правильним Sender
                                    if (receivedClientMessage.Type == MessageType.FileTransferMetadata ||
                                        receivedClientMessage.Type == MessageType.FileTransferChunk ||
                                        receivedClientMessage.Type == MessageType.FileTransferEnd)
                                    {
                                        receivedClientMessage.Sender = _nickname; // Встановлюємо відправника
                                        await _server.BroadcastMessageAsync(receivedClientMessage, this);
                                    }
                                    else
                                    {
                                        Console.WriteLine($"Сервер: Невідомий або необроблений тип повідомлення {receivedClientMessage.Type} від {_nickname}.");
                                    }
                                    break;
                            }
                        }
                        catch (JsonSerializationException ex)
                        {
                            Console.WriteLine($"Сервер: Помилка десеріалізації JSON від {_nickname}: {ex.Message}. Пропускаємо. Зашифроване: '{fullEncryptedMessage.Substring(0, Math.Min(fullEncryptedMessage.Length, 100))}'... Розшифроване (спроба): '{TryDecrypt(fullEncryptedMessage)}'");
                        }
                        catch (FormatException ex)
                        {
                            Console.WriteLine($"Сервер: Помилка формату Base64 повідомлення від {_nickname}: {ex.Message}. Пропускаємо. Зашифроване: '{fullEncryptedMessage.Substring(0, Math.Min(fullEncryptedMessage.Length, 100))}'...");
                        }
                        catch (CryptographicException ex)
                        {
                            Console.WriteLine($"Сервер: Помилка шифрування повідомлення від {_nickname}: {ex.Message}. Пропускаємо. Зашифроване: '{fullEncryptedMessage.Substring(0, Math.Min(fullEncryptedMessage.Length, 100))}'...");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Сервер: Неочікувана помилка при обробці повідомлення від {_nickname}: {ex.Message}. Пропускаємо.");
                        }
                    }
                }
            }
            catch (System.IO.IOException ex)
            {
                Console.WriteLine($"Сервер: IOException для клієнта {_client.Client.RemoteEndPoint} ({_nickname}): {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Сервер: Неочікувана помилка обробки клієнта {_client.Client.RemoteEndPoint} ({_nickname}): {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                // Console.WriteLine($"Сервер: Клієнт {_client.Client.RemoteEndPoint} ({_nickname}) переходить до блоку finally.");
                if (!string.IsNullOrEmpty(_nickname) && _nickname != "UnknownUser")
                {
                    await _server.BroadcastMessageAsync(new ChatMessage { Type = MessageType.SystemMessage, Sender = "Server", Content = $"[{_nickname}] покинув чат." }, this);
                }
                _server.RemoveClient(this);
                _client.Close();
                Console.WriteLine($"Сервер: Клієнт {_client.Client.RemoteEndPoint} ({_nickname}) відключився та ресурси закрито.");
            }
        }
        private string TryDecrypt(string encryptedText) // Допоміжний метод для логування
        {
            try { return EncryptionHelper.Decrypt(encryptedText); }
            catch { return "[не вдалося розшифрувати]"; }
        }
    }
}