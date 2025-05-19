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
            _clientId = client.Client.RemoteEndPoint.ToString(); // Зберігаємо для логування
            _chatDatabase = chatDatabase;
            Console.WriteLine($"Сервер: Створено новий ClientHandler для {_clientId}");
        }

        public TcpClient TcpClient => _client;
        public string Nickname => _nickname;

        public async Task ProcessClientAsync()
        {
            NetworkStream stream = null; // Ініціалізуємо null для finally
            // ВАЖЛИВО: receivedDataBuilder має бути ЛОКАЛЬНИМ для цього методу,
            // щоб не було перетину даних між різними асинхронними викликами ProcessClientAsync,
            // хоча ClientHandler і створюється на кожного клієнта.
            StringBuilder receivedDataBuilder = new StringBuilder();
            byte[] buffer = new byte[4096];
            int bytesRead;

            Console.WriteLine($"Сервер [{_clientId}]: Початок ProcessClientAsync.");

            try
            {
                stream = _client.GetStream(); // Отримуємо потік тут
                string encryptedFirstMessage = null;

                Console.WriteLine($"Сервер [{_clientId}]: Очікування першого повідомлення (нікнейм)...");
                // Цикл для читання першого повідомлення до '\n'
                while (_client.Connected && encryptedFirstMessage == null)
                {
                    if (!stream.CanRead)
                    {
                        Console.WriteLine($"Сервер [{_clientId}]: Потік більше не доступний для читання. Відключення.");
                        return;
                    }
                    bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    Console.WriteLine($"Сервер [{_clientId}]: Прочитано {bytesRead} байт з потоку.");

                    if (bytesRead == 0)
                    {
                        Console.WriteLine($"Сервер [{_clientId}]: Клієнт відключився (bytesRead == 0) до надсилання повного першого повідомлення.");
                        return;
                    }

                    string receivedChunk = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    Console.WriteLine($"Сервер [{_clientId}]: Отримано фрагмент: '{receivedChunk.Replace("\n", "\\n")}'"); // Логуємо фрагмент, екрануючи \n
                    receivedDataBuilder.Append(receivedChunk);
                    Console.WriteLine($"Сервер [{_clientId}]: Поточний вміст receivedDataBuilder: '{receivedDataBuilder.ToString().Replace("\n", "\\n")}'");

                    string currentFullBuffer = receivedDataBuilder.ToString();
                    int newlineIndex = currentFullBuffer.IndexOf('\n');
                    if (newlineIndex != -1)
                    {
                        encryptedFirstMessage = currentFullBuffer.Substring(0, newlineIndex).Trim(); // Додамо Trim() про всяк випадок
                        Console.WriteLine($"Сервер [{_clientId}]: Виділено перше повідомлення (до '\\n'): '{encryptedFirstMessage}' (Довжина: {encryptedFirstMessage.Length})");

                        // Видаляємо оброблене повідомлення та символ '\n' з буфера
                        receivedDataBuilder.Remove(0, newlineIndex + 1);
                        Console.WriteLine($"Сервер [{_clientId}]: Залишок у receivedDataBuilder: '{receivedDataBuilder.ToString().Replace("\n", "\\n")}'");
                    }
                    else
                    {
                        Console.WriteLine($"Сервер [{_clientId}]: Символ '\\n' ще не знайдено в буфері.");
                    }
                }

                if (string.IsNullOrWhiteSpace(encryptedFirstMessage)) // Якщо після Trim нічого не залишилося
                {
                    Console.WriteLine($"Сервер [{_clientId}]: Перше повідомлення порожнє або складається з пробілів після Trim. Відключення.");
                    return;
                }

                // Подальша обробка encryptedFirstMessage
                ChatMessage initialMessage = null;
                Console.WriteLine($"СЕРВЕР [{_clientId}]: Перше повідомлення (зашифроване, після Trim) для обробки: '{encryptedFirstMessage}'");
                try
                {
                    string decryptedJson = EncryptionHelper.Decrypt(encryptedFirstMessage);
                    Console.WriteLine($"СЕРВЕР [{_clientId}]: Перше повідомлення (розшифроване JSON) від {_clientId}: '{decryptedJson}'");

                    // Перевірка, чи не є розшифрований JSON порожнім
                    if (string.IsNullOrWhiteSpace(decryptedJson))
                    {
                        Console.WriteLine($"СЕРВЕР [{_clientId}]: Розшифрований JSON порожній. Відключення.");
                        return;
                    }

                    initialMessage = ChatMessage.FromJson(decryptedJson); // Тут виникала помилка "JSON array"

                    if (initialMessage == null) // Якщо FromJson повернув null
                    {
                        Console.WriteLine($"Сервер [{_clientId}]: Не вдалося десеріалізувати перше повідомлення (FromJson повернув null). JSON: '{decryptedJson}'. Відключення.");
                        return;
                    }

                    if (initialMessage.Type != MessageType.SystemMessage || string.IsNullOrWhiteSpace(initialMessage.Sender))
                    {
                        Console.WriteLine($"Сервер [{_clientId}]: Надіслано недійсне перше повідомлення (тип: {initialMessage.Type}, sender: '{initialMessage.Sender}') або відсутній нікнейм. JSON: '{decryptedJson}'. Відключення.");
                        return;
                    }

                    if (_server.IsNicknameTaken(initialMessage.Sender))
                    {
                        Console.WriteLine($"Сервер [{_clientId}]: Нікнейм '{initialMessage.Sender}' вже зайнятий. Відключення.");
                        var errorMessage = new ChatMessage
                        {
                            Type = MessageType.SystemMessage,
                            Sender = "Server",
                            Content = "Нікнейм вже зайнятий, спробуйте інший!"
                        };
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

                    // ... (код надсилання історії) ...
                }
                catch (JsonSerializationException ex)
                {
                    Console.WriteLine($"Сервер [{_clientId}]: Помилка ДЕСЕРІАЛІЗАЦІЇ JSON першого повідомлення: {ex.Message}. Зашифроване: '{encryptedFirstMessage}'. Розшифроване (спроба): '{TryDecrypt(encryptedFirstMessage)}'. Відключення.");
                    return;
                }
                // ... (інші catch блоки для FormatException, CryptographicException, Exception) ...
                catch (FormatException ex)
                {
                    Console.WriteLine($"Сервер [{_clientId}]: Помилка ФОРМАТУ (Base64) першого повідомлення: {ex.Message}. Зашифроване: '{encryptedFirstMessage}'. Відключення.");
                    return;
                }
                catch (CryptographicException ex)
                {
                    Console.WriteLine($"Сервер [{_clientId}]: Помилка ДЕШИФРУВАННЯ першого повідомлення: {ex.Message}. Зашифроване: '{encryptedFirstMessage}'. Відключення.");
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Сервер [{_clientId}]: НЕОЧІКУВАНА помилка при обробці першого повідомлення: {ex.Message}. Зашифроване: '{encryptedFirstMessage}'. Стек: {ex.StackTrace}. Відключення.");
                    return;
                }

                // === Основний цикл читання наступних повідомлень ===
                // Важливо, що receivedDataBuilder тепер містить залишок після першого повідомлення
                Console.WriteLine($"Сервер [{_clientId} ({_nickname})]: Перехід до основного циклу читання повідомлень. Залишок у буфері: '{receivedDataBuilder.ToString().Replace("\n", "\\n")}'");
                while (_client.Connected)
                {
                    // Спочатку обробляємо те, що могло залишитися в receivedDataBuilder
                    string currentProcessingBuffer = receivedDataBuilder.ToString();
                    receivedDataBuilder.Clear(); // Очищуємо, бо зараз будемо доповнювати з нового читання або обробимо залишок

                    int nextNewlineIndex;
                    while ((nextNewlineIndex = currentProcessingBuffer.IndexOf('\n')) != -1)
                    {
                        string fullEncryptedMessage = currentProcessingBuffer.Substring(0, nextNewlineIndex).Trim();
                        currentProcessingBuffer = currentProcessingBuffer.Substring(nextNewlineIndex + 1); // Залишок для наступної ітерації цього while

                        if (string.IsNullOrWhiteSpace(fullEncryptedMessage)) continue;

                        Console.WriteLine($"Сервер [{_clientId} ({_nickname})]: Оброблено повідомлення з буфера: '{fullEncryptedMessage}'");
                        await ProcessSingleMessageAsync(fullEncryptedMessage, stream); // Виносимо обробку одного повідомлення в окремий метод
                    }
                    // Залишок без '\n' повертаємо в receivedDataBuilder для наступного читання
                    receivedDataBuilder.Append(currentProcessingBuffer);

                    // Тепер читаємо нові дані з потоку
                    if (!stream.CanRead) break;
                    bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    Console.WriteLine($"Сервер [{_clientId} ({_nickname})]: Прочитано {bytesRead} байт з потоку (основний цикл).");

                    if (bytesRead == 0)
                    {
                        Console.WriteLine($"Сервер [{_clientId} ({_nickname})]: Клієнт відключився (bytesRead == 0 в основному циклі).");
                        break;
                    }
                    string newChunk = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    Console.WriteLine($"Сервер [{_clientId} ({_nickname})]: Отримано новий фрагмент в основному циклі: '{newChunk.Replace("\n", "\\n")}'");
                    receivedDataBuilder.Append(newChunk);
                }
            }
            catch (System.IO.IOException ex)
            {
                Console.WriteLine($"Сервер [{_clientId} ({_nickname})]: IOException: {ex.Message}. З'єднання, ймовірно, розірвано.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Сервер [{_clientId} ({_nickname})]: Неочікувана помилка в ProcessClientAsync: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                Console.WriteLine($"Сервер [{_clientId} ({_nickname})]: Блок finally в ProcessClientAsync.");
                if (stream != null) { try { stream.Close(); stream.Dispose(); } catch { /* ігноруємо помилки закриття */ } }
                if (_client != null) { try { _client.Close(); _client.Dispose(); } catch { /* ігноруємо помилки закриття */ } }

                // Повідомляємо про відключення та оновлюємо список користувачів
                // Це робиться в RemoveClient, який має викликатися звідси або з TcpServer
                _server.RemoveClient(this); // Це викличе SendUserListAsync та повідомить інших
                Console.WriteLine($"Сервер [{_clientId} ({_nickname})]: Клієнта видалено та ресурси закрито.");
            }
        }

        // Новий метод для обробки одного розшифрованого та десеріалізованого повідомлення
        private async Task ProcessSingleMessageAsync(string encryptedMessage, NetworkStream stream)
        {
            try
            {
                string decryptedJson = EncryptionHelper.Decrypt(encryptedMessage);
                Console.WriteLine($"Сервер [{_clientId} ({_nickname})]: Розшифровано (наступне повідомлення): '{decryptedJson}'");
                ChatMessage clientMessage = ChatMessage.FromJson(decryptedJson);

                if (clientMessage == null)
                {
                    Console.WriteLine($"Сервер [{_clientId} ({_nickname})]: Не вдалося десеріалізувати наступне повідомлення. JSON: '{decryptedJson}'.");
                    return;
                }

                // Встановлюємо Sender на сервері для безпеки та коректності
                clientMessage.Sender = _nickname;
                clientMessage.Timestamp = DateTime.Now; // Серверний час

                switch (clientMessage.Type)
                {
                    case MessageType.ChatMessage:
                        _chatDatabase.SaveMessage(clientMessage.Timestamp, clientMessage.Sender, clientMessage.Content);
                        await _server.BroadcastMessageAsync(clientMessage, this);
                        break;
                    case MessageType.Disconnect:
                        Console.WriteLine($"Сервер [{_clientId} ({_nickname})]: Клієнт ініціював відключення через повідомлення.");
                        // Тут потрібно викликати логіку закриття з'єднання для цього ClientHandler
                        // Найпростіше - просто вийти, finally в ProcessClientAsync все зробить
                        if (_client.Connected) _client.Close(); // Це призведе до виходу з основного циклу ProcessClientAsync
                        break;
                    case MessageType.FileTransferMetadata:
                    case MessageType.FileTransferChunk:
                    case MessageType.FileTransferEnd:
                        // Просто транслюємо далі, Sender вже встановлено
                        await _server.BroadcastMessageAsync(clientMessage, this);
                        break;
                    default:
                        Console.WriteLine($"Сервер [{_clientId} ({_nickname})]: Невідомий тип повідомлення {clientMessage.Type}.");
                        break;
                }
            }
            catch (JsonSerializationException ex) { Console.WriteLine($"Сервер [{_clientId} ({_nickname})]: Помилка десеріалізації (наступне повідомлення): {ex.Message}. Зашифроване: '{encryptedMessage}'. Розшифроване (спроба): '{TryDecrypt(encryptedMessage)}'"); }
            catch (FormatException ex) { Console.WriteLine($"Сервер [{_clientId} ({_nickname})]: Помилка формату (наступне повідомлення): {ex.Message}. Зашифроване: '{encryptedMessage}'."); }
            catch (CryptographicException ex) { Console.WriteLine($"Сервер [{_clientId} ({_nickname})]: Помилка дешифрування (наступне повідомлення): {ex.Message}. Зашифроване: '{encryptedMessage}'."); }
            catch (Exception ex) { Console.WriteLine($"Сервер [{_clientId} ({_nickname})]: Неочікувана помилка обробки (наступне повідомлення): {ex.Message}. Зашифроване: '{encryptedMessage}'."); }
        }

        private string TryDecrypt(string encryptedText)
        {
            try { return EncryptionHelper.Decrypt(encryptedText); }
            catch { return "[не вдалося розшифрувати]"; }
        }
    }
}