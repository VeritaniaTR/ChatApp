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
                Debug.WriteLine($"Клієнт ({_clientNickname}): Спроба підключення, коли вже підключено.");
                ConnectionStatusChanged?.Invoke(true);
                return;
            }
            if (_client != null && !_client.Connected)
            {
                Debug.WriteLine($"Клієнт ({_clientNickname}): Існує попередній об'єкт TcpClient, але не підключений. Знищення старого.");
                try { _client.Dispose(); } catch { /* ігнор */ }
                _client = null;
            }

            Debug.WriteLine($"Клієнт ({_clientNickname}): Спроба підключення до {serverIp}:{serverPort}...");
            try
            {
                _client = new TcpClient();
                await _client.ConnectAsync(_serverIp, _serverPort);
                _stream = _client.GetStream();
                Debug.WriteLine($"Клієнт ({_clientNickname}): Успішно підключено до сокета.");

                var connectMessage = new ChatMessage
                {
                    Type = MessageType.SystemMessage,
                    Sender = nickname,
                    Content = "Підключення клієнта..."
                };
                string jsonConnectMessage = connectMessage.ToJson();
                Debug.WriteLine($"КЛІЄНТ ({_clientNickname}): Перше повідомлення JSON для надсилання: {jsonConnectMessage}");
                string encryptedData = EncryptionHelper.Encrypt(jsonConnectMessage);
                byte[] data = Encoding.UTF8.GetBytes(encryptedData + "\n");

                await _stream.WriteAsync(data, 0, data.Length);
                await _stream.FlushAsync();
                Debug.WriteLine($"Клієнт ({_clientNickname}): Повідомлення про підключення надіслано.");

                ConnectionStatusChanged?.Invoke(true);
                StartReceiving();
            }
            catch (SocketException ex)
            {
                Debug.WriteLine($"Клієнт ({_clientNickname}): Помилка сокета при підключенні: {ex.Message} (Код: {ex.SocketErrorCode})\n{ex.StackTrace}");
                ConnectionStatusChanged?.Invoke(false);
                await SafeDisconnectAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Клієнт ({_clientNickname}): Неочікувана помилка при підключенні: {ex.Message}\n{ex.StackTrace}");
                ConnectionStatusChanged?.Invoke(false);
                await SafeDisconnectAsync();
            }
        }

        public async Task SendMessageObjectAsync(ChatMessage messageObject)
        {
            if (!IsConnected || _stream == null)
            {
                Debug.WriteLine($"Клієнт ({_clientNickname}): Спроба надіслати повідомлення '{messageObject.Type}' без активного з'єднання.");
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
                Debug.WriteLine($"Клієнт ({_clientNickname}): Помилка надсилання об'єкта повідомлення '{messageObject.Type}': {ex.Message}");
                if (ex is System.IO.IOException || ex is SocketException)
                {
                    Debug.WriteLine($"Клієнт ({_clientNickname}): Серйозна помилка надсилання, ініціюю відключення.");
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
            Debug.WriteLine($"Клієнт ({_clientNickname}): Запуск потоку отримання повідомлень (StartReceiving).");
            Task.Run(async () => await ReceiveAsync()).ContinueWith(t =>
            {
                if (t.IsFaulted && t.Exception != null)
                {
                    var baseException = t.Exception.GetBaseException();
                    Debug.WriteLine($"КЛІЄНТ ({_clientNickname}): КРИТИЧНА ПОМИЛКА В ПОТОЦІ ОТРИМАННЯ (ReceiveAsync): {baseException.Message}\nСТЕК: {baseException.StackTrace}");
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        ConnectionStatusChanged?.Invoke(false);
                    });
                    // Не викликаємо SafeDisconnectAsync звідси, оскільки finally в ReceiveAsync має це зробити
                }
                else if (t.IsCanceled)
                {
                    Debug.WriteLine($"КЛІЄНТ ({_clientNickname}): Потік отримання було скасовано.");
                }
                // Не логуємо "завершив роботу штатно", бо це може статися при нормальному відключенні
            }, TaskContinuationOptions.ExecuteSynchronously); // ExecuteSynchronously, щоб гарантувати виконання до виходу з програми, якщо це головний потік (тут не зовсім так, але може допомогти з логуванням)
        }

        private async Task ReceiveAsync()
        {
            StringBuilder receivedDataBuilder = new StringBuilder(); // Локальний буфер для цього виклику
            byte[] buffer = new byte[8192];
            Debug.WriteLine($"Клієнт ({_clientNickname}): Потік ReceiveAsync розпочато, очікування даних...");
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
                        Debug.WriteLine($"Клієнт ({_clientNickname}): IOException під час читання з потоку в ReceiveAsync: {ex.Message}. З'єднання буде закрито.");
                        break;
                    }
                    catch (ObjectDisposedException ex)
                    {
                        Debug.WriteLine($"Клієнт ({_clientNickname}): ObjectDisposedException під час читання з потоку в ReceiveAsync: {ex.Message}. З'єднання буде закрито.");
                        break;
                    }

                    if (bytesRead == 0)
                    {
                        Debug.WriteLine($"Клієнт ({_clientNickname}): Сервер закрив з'єднання або з'єднання втрачено (bytesRead == 0) в ReceiveAsync.");
                        break;
                    }

                    string receivedChunk = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    // Debug.WriteLine($"Клієнт ({_clientNickname}): Отримано фрагмент в ReceiveAsync: '{receivedChunk.Replace("\n", "\\n")}'");
                    receivedDataBuilder.Append(receivedChunk);
                    string currentBufferContent = receivedDataBuilder.ToString();
                    int newlineIndex;

                    while ((newlineIndex = currentBufferContent.IndexOf('\n')) != -1)
                    {
                        string fullEncryptedMessage = currentBufferContent.Substring(0, newlineIndex).Trim(); // Trim!
                        receivedDataBuilder.Remove(0, newlineIndex + 1);
                        currentBufferContent = receivedDataBuilder.ToString();

                        if (string.IsNullOrWhiteSpace(fullEncryptedMessage))
                        {
                            Debug.WriteLine($"Клієнт ({_clientNickname}): Пропущено порожнє повідомлення після Trim.");
                            continue;
                        }

                        // Debug.WriteLine($"Клієнт ({_clientNickname}): Обробка повідомлення: '{fullEncryptedMessage.Substring(0, Math.Min(fullEncryptedMessage.Length, 50))}'...");
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
                                Debug.WriteLine($"Клієнт ({_clientNickname}): Не вдалося десеріалізувати JSON в ChatMessage. JSON: '{decryptedJson.Substring(0, Math.Min(decryptedJson.Length, 100))}'...");
                            }
                        }
                        catch (Exception ex_inner_loop) // Ловимо всі помилки обробки одного повідомлення
                        {
                            Debug.WriteLine($"Клієнт ({_clientNickname}): Помилка обробки одного повідомлення: {ex_inner_loop.GetType().Name} - {ex_inner_loop.Message}. Зашифроване: '{fullEncryptedMessage.Substring(0, Math.Min(fullEncryptedMessage.Length, 100))}'... Розшифроване (спроба): '{TryDecrypt(fullEncryptedMessage)}'");
                        }
                    }
                }
            }
            catch (Exception ex_outer_receive_loop)
            {
                Debug.WriteLine($"Клієнт ({_clientNickname}): Неочікувана зовнішня помилка в ReceiveAsync: {ex_outer_receive_loop.Message}\nСТЕК: {ex_outer_receive_loop.StackTrace}");
                throw; // Перекидаємо, щоб зловив ContinueWith
            }
            finally
            {
                Debug.WriteLine($"Клієнт ({_clientNickname}): Блок finally в ReceiveAsync. Поточний стан IsConnected: {IsConnected}");
                // Важливо: SafeDisconnectAsync викличе ConnectionStatusChanged(false)
                // Якщо ми тут, значить цикл завершився (сокет закрито або помилка читання)
                await SafeDisconnectAsync();
                Debug.WriteLine($"Клієнт ({_clientNickname}): Роботу ReceiveAsync завершено, SafeDisconnectAsync викликано.");
            }
        }

        private string TryDecrypt(string encryptedText)
        {
            try { return EncryptionHelper.Decrypt(encryptedText); }
            catch { return "[не вдалося розшифрувати для логу]"; }
        }

        public async Task DisconnectAsync()
        {
            Debug.WriteLine($"Клієнт ({_clientNickname}): ViewModel ініціював DisconnectAsync. IsConnected: {IsConnected}");
            if (!IsConnected && _client == null)
            {
                Debug.WriteLine($"Клієнт ({_clientNickname}): DisconnectAsync: вже відключено або не було підключено.");
                ConnectionStatusChanged?.Invoke(false);
                return;
            }

            bool wasConnected = IsConnected; // Запам'ятовуємо стан

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
                    Debug.WriteLine($"Клієнт ({_clientNickname}): Повідомлення про відключення надіслано на сервер.");
                }
                catch (Exception ex) when (ex is System.IO.IOException || ex is SocketException || ex is ObjectDisposedException)
                {
                    Debug.WriteLine($"Клієнт ({_clientNickname}): Помилка (IO/Socket/Disposed) при надсиланні DisconnectMessage: {ex.Message}.");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Клієнт ({_clientNickname}): Загальна помилка при надсиланні DisconnectMessage: {ex.Message}.");
                }
            }
            await CloseClientResources(); // Закриваємо ресурси
        }

        private async Task SafeDisconnectAsync()
        {
            // Цей метод має бути максимально безпечним і не кидати виключень
            if (_client == null && _stream == null) // Якщо вже все закрито
            {
                ConnectionStatusChanged?.Invoke(false); // Просто оновити статус
                return;
            }
            Debug.WriteLine($"Клієнт ({_clientNickname}): Виклик SafeDisconnectAsync.");
            await CloseClientResources();
        }

        private async Task CloseClientResources()
        {
            // Перевіряємо, чи є що закривати, щоб уникнути ObjectDisposedException, якщо вже закрито
            bool resourcesWereOpen = (_stream != null || _client != null);

            NetworkStream tempStream = _stream;
            TcpClient tempClient = _client;
            _stream = null;
            _client = null;

            try { tempStream?.Close(); tempStream?.Dispose(); } catch (Exception ex) { Debug.WriteLine($"Клієнт ({_clientNickname}): Помилка при закритті потоку в CloseClientResources: {ex.Message}"); }
            try { tempClient?.Close(); tempClient?.Dispose(); } catch (Exception ex) { Debug.WriteLine($"Клієнт ({_clientNickname}): Помилка при закритті клієнта в CloseClientResources: {ex.Message}"); }

            if (resourcesWereOpen) // Викликаємо ConnectionStatusChanged тільки якщо ресурси дійсно були відкриті і ми їх зараз закрили
            {
                Debug.WriteLine($"Клієнт ({_clientNickname}): Ресурси закрито в CloseClientResources, викликаємо ConnectionStatusChanged(false).");
                ConnectionStatusChanged?.Invoke(false);
            }
        }

        [Obsolete("Використовуйте DisconnectAsync або SafeDisconnectAsync.")]
        public void Disconnect()
        {
            Debug.WriteLine($"Клієнт ({_clientNickname}): Застарілий метод Disconnect() викликано. Перенаправлення на DisconnectAsync.");
            Task.Run(async () => await DisconnectAsync());
        }
    }
}