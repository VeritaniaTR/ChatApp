using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using ChatApp.Server.Networking;
using ChatApp.Common.Utilities;
using System.Security.Cryptography;
using ChatApp.Common.Models; // Додано для ChatMessage
using Newtonsoft.Json; // Додано для JsonSerializationException

namespace ChatApp.Server.Core
{
    public class ClientHandler
    {
        private readonly TcpServer _server;
        private TcpClient _client;
        private string _clientId;
        private string _nickname = "UnknownUser"; // Нікнейм за замовчуванням
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

            int bytesRead; // Оголошено тут

            try
            {
                // === ЗМІНА: Більш надійна обробка першого повідомлення (нікнейму) ===
                string encryptedFirstMessage = null;
                // Читаємо дані, поки не отримаємо повний зашифрований нікнейм, завершений '\n'
                // Або поки клієнт не відключиться
                while (_client.Connected && encryptedFirstMessage == null)
                {
                    bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) // З'єднання закрито клієнтом до надсилання першого повідомлення
                    {
                        Console.WriteLine($"Сервер: Клієнт {_client.Client.RemoteEndPoint} відключився до надсилання першого повідомлення.");
                        return; // Вихід, щоб потрапити в finally
                    }
                    receivedDataBuilder.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));

                    int newlineIndex = receivedDataBuilder.ToString().IndexOf('\n');
                    if (newlineIndex != -1)
                    {
                        encryptedFirstMessage = receivedDataBuilder.ToString().Substring(0, newlineIndex);
                        receivedDataBuilder.Remove(0, newlineIndex + 1); // Видаляємо нікнейм з буфера
                    }
                    // Якщо newlineIndex == -1, продовжуємо читати, поки не знайдемо \n
                }

                ChatMessage initialMessage = null;
                if (encryptedFirstMessage != null)
                {
                    try
                    {
                        string decryptedJson = EncryptionHelper.Decrypt(encryptedFirstMessage);
                        initialMessage = ChatMessage.FromJson(decryptedJson);

                        if (initialMessage == null || initialMessage.Type != MessageType.SystemMessage || string.IsNullOrWhiteSpace(initialMessage.Sender))
                        {
                            Console.WriteLine($"Сервер: Клієнт {_client.Client.RemoteEndPoint} надіслав недійсне перше повідомлення або відсутній нікнейм. Відключення.");
                            return;
                        }

                        // Перевірка на унікальність нікнейму
                        if (_server.IsNicknameTaken(initialMessage.Sender))
                        {
                            Console.WriteLine($"Сервер: Нікнейм '{initialMessage.Sender}' вже зайнятий. Відключення клієнта {_client.Client.RemoteEndPoint}.");
                            // Надсилаємо клієнту повідомлення про помилку
                            var errorMessage = new ChatMessage
                            {
                                Type = MessageType.SystemMessage,
                                Sender = "Server",
                                Content = "Нікнейм вже зайнятий, спробуйте інший!"
                            };
                            string encryptedError = EncryptionHelper.Encrypt(errorMessage.ToJson());
                            byte[] errorData = Encoding.UTF8.GetBytes(encryptedError + "\n");
                            await stream.WriteAsync(errorData, 0, errorData.Length);
                            await Task.Delay(50); // Даємо час на відправку
                            return; // Вихід
                        }

                        _nickname = initialMessage.Sender; // Встановлюємо нікнейм, якщо він унікальний

                        Console.WriteLine($"Сервер: Клієнт {_client.Client.RemoteEndPoint} встановив нікнейм: {_nickname}");
                        await _server.BroadcastMessageAsync(new ChatMessage { Type = MessageType.SystemMessage, Content = $"[{_nickname}] приєднався до чату." }, this);

                        // Завантажуємо та надсилаємо історію чату новому клієнту
                        List<string> chatHistory = _chatDatabase.GetMessageHistory(50);
                        foreach (var historyMessageContent in chatHistory)
                        {
                            var historyChatMessage = new ChatMessage
                            {
                                Type = MessageType.ChatMessage,
                                Sender = "Server", // Для історичних повідомлень, відправником може бути "Server" або оригінальний відправник
                                Content = historyMessageContent // Зміст повідомлення вже містить нікнейм та час
                            };
                            string encryptedHistoryMessage = EncryptionHelper.Encrypt(historyChatMessage.ToJson());
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
                    catch (JsonSerializationException ex)
                    {
                        Console.WriteLine($"Сервер: Помилка десеріалізації JSON першого повідомлення від {_client.Client.RemoteEndPoint}: {ex.Message}. Відключення.");
                        return;
                    }
                    catch (FormatException ex)
                    {
                        Console.WriteLine($"Сервер: Помилка формату Base64 першого повідомлення від {_client.Client.RemoteEndPoint}. Відключення. {ex.Message}");
                        return;
                    }
                    catch (CryptographicException ex)
                    {
                        Console.WriteLine($"Сервер: Помилка шифрування першого повідомлення від {_client.Client.RemoteEndPoint}. Відключення. {ex.Message}");
                        return;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Сервер: Неочікувана помилка при отриманні першого повідомлення від {_client.Client.RemoteEndPoint}: {ex.Message}. Відключення.");
                        return;
                    }
                }
                else
                {
                    Console.WriteLine($"Сервер: Клієнт {_client.Client.RemoteEndPoint} не надіслав перше повідомлення до закриття з'єднання. Відключення.");
                    return;
                }

                // Основний цикл читання повідомлень
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

                        try
                        {
                            string decryptedJson = EncryptionHelper.Decrypt(fullEncryptedMessage).Trim();
                            ChatMessage receivedObject = ChatMessage.FromJson(decryptedJson);

                            if (receivedObject == null)
                            {
                                Console.WriteLine($"Сервер: Отримано недійсний JSON від {_nickname}. Пропускаємо.");
                                continue;
                            }

                            Console.WriteLine($"Сервер: Отримано від {_nickname} (Тип: {receivedObject.Type}): {receivedObject.Content}");

                            switch (receivedObject.Type)
                            {
                                case MessageType.ChatMessage:
                                    _chatDatabase.SaveMessage(receivedObject.Timestamp, _nickname, receivedObject.Content);
                                    await _server.BroadcastMessageAsync(new ChatMessage { Type = MessageType.ChatMessage, Sender = _nickname, Content = receivedObject.Content }, this);
                                    break;
                                case MessageType.Disconnect:
                                    Console.WriteLine($"Сервер: Клієнт {_nickname} ініціював відключення.");
                                    return;
                                case MessageType.PrivateMessage:
                                    // Буде реалізовано пізніше
                                    break;
                                case MessageType.FileTransfer:
                                    // Буде реалізовано пізніше
                                    break;
                                case MessageType.TypingStatus:
                                    // Буде реалізовано пізніше
                                    break;
                                default:
                                    Console.WriteLine($"Сервер: Невідомий тип повідомлення від {_nickname}: {receivedObject.Type}");
                                    break;
                            }
                        }
                        catch (JsonSerializationException ex)
                        {
                            Console.WriteLine($"Сервер: Помилка десеріалізації JSON від {_nickname}. Пропускаємо. {ex.Message}");
                        }
                        catch (FormatException ex)
                        {
                            Console.WriteLine($"Сервер: Помилка формату Base64 повідомлення від {_nickname}. Пропускаємо. {ex.Message}");
                        }
                        catch (CryptographicException ex)
                        {
                            Console.WriteLine($"Сервер: Помилка шифрування повідомлення від {_nickname}. Пропускаємо. {ex.Message}");
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
                Console.WriteLine($"Сервер: Помилка читання від {_client.Client.RemoteEndPoint} ({_nickname}): {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Сервер: Неочікувана помилка обробки клієнта {_client.Client.RemoteEndPoint} ({_nickname}): {ex.Message}");
            }
            finally
            {
                // Повідомляємо всіх про те, що клієнт покинув чат
                if (!string.IsNullOrEmpty(_nickname) && _nickname != "UnknownUser")
                {
                    await _server.BroadcastMessageAsync(new ChatMessage { Type = MessageType.SystemMessage, Content = $"[{_nickname}] покинув чат." }, this);
                }
                _server.RemoveClient(this);
                _client.Close();
                Console.WriteLine($"Сервер: Клієнт {_client.Client.RemoteEndPoint} ({_nickname}) відключився.");
            }
        }
    }
}