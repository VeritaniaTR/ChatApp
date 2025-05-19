using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using ChatApp.Common.Utilities;
using System.Security.Cryptography;
using ChatApp.Common.Models;
using Newtonsoft.Json;
using System.Diagnostics; // Для Debug.WriteLine

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
                _client.Dispose();
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
                // === ЗМІНА: Додано логування JSON, що надсилається ===
                Debug.WriteLine($"КЛІЄНТ ({_clientNickname}): Перше повідомлення JSON для надсилання: {jsonConnectMessage}");
                // =====================================================
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
                Debug.WriteLine($"Клієнт ({_clientNickname}): Помилка сокета при підключенні: {ex.Message} (Код: {ex.SocketErrorCode})");
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

        // ... (SendMessageObjectAsync, SendMessageAsync - без змін відносно попередньої версії з логуванням) ...
        public async Task SendMessageObjectAsync(ChatMessage messageObject)
        {
            if (!IsConnected || _stream == null)
            {
                Debug.WriteLine($"Клієнт ({_clientNickname}): Спроба надіслати повідомлення без активного з'єднання.");
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
                Debug.WriteLine($"Клієнт ({_clientNickname}): Помилка надсилання об'єкта повідомлення: {ex.Message}");
                if (ex is System.IO.IOException || ex is SocketException)
                {
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
            Debug.WriteLine($"Клієнт ({_clientNickname}): Запуск потоку отримання повідомлень.");
            Task.Run(async () => await ReceiveAsync()).ContinueWith(t =>
            {
                if (t.IsFaulted && t.Exception != null)
                {
                    var baseException = t.Exception.GetBaseException();
                    Debug.WriteLine($"КЛІЄНТ ({_clientNickname}): КРИТИЧНА ПОМИЛКА В ПОТОЦІ ОТРИМАННЯ: {baseException.Message}\nСТЕК: {baseException.StackTrace}");

                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        ConnectionStatusChanged?.Invoke(false);
                    });
                    Task.Run(async () => await SafeDisconnectAsync());
                }
                else if (t.IsCanceled)
                {
                    Debug.WriteLine($"КЛІЄНТ ({_clientNickname}): Потік отримання було скасовано.");
                }
                else
                {
                    // Це логування може бути зайвим, якщо відключення штатне
                    // Debug.WriteLine($"КЛІЄНТ ({_clientNickname}): Потік отримання завершив роботу.");
                }
            });
        }

        private async Task ReceiveAsync()
        {
            try
            {
                byte[] buffer = new byte[8192];
                StringBuilder receivedDataBuilder = new StringBuilder();
                // Debug.WriteLine($"Клієнт ({_clientNickname}): Потік ReceiveAsync розпочато, очікування даних...");

                while (_client != null && _client.Connected && _stream != null && _stream.CanRead)
                {
                    int bytesRead = 0;
                    try
                    {
                        bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
                    }
                    catch (System.IO.IOException ex)
                    {
                        Debug.WriteLine($"Клієнт ({_clientNickname}): IOException під час читання з потоку: {ex.Message}. З'єднання буде закрито.");
                        break;
                    }
                    catch (ObjectDisposedException ex)
                    {
                        Debug.WriteLine($"Клієнт ({_clientNickname}): ObjectDisposedException під час читання з потоку: {ex.Message}. З'єднання буде закрито.");
                        break;
                    }

                    if (bytesRead == 0)
                    {
                        Debug.WriteLine($"Клієнт ({_clientNickname}): Сервер закрив з'єднання або з'єднання втрачено (bytesRead == 0).");
                        break;
                    }

                    receivedDataBuilder.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
                    string currentBufferContent = receivedDataBuilder.ToString();
                    int newlineIndex;

                    while ((newlineIndex = currentBufferContent.IndexOf('\n')) != -1)
                    {
                        string fullEncryptedMessage = currentBufferContent.Substring(0, newlineIndex);
                        receivedDataBuilder.Remove(0, newlineIndex + 1);
                        currentBufferContent = receivedDataBuilder.ToString();

                        if (string.IsNullOrWhiteSpace(fullEncryptedMessage)) continue;

                        try
                        {
                            string decryptedJson = EncryptionHelper.Decrypt(fullEncryptedMessage);
                            ChatMessage receivedObject = ChatMessage.FromJson(decryptedJson);

                            if (receivedObject != null)
                            {
                                MessageReceived?.Invoke(receivedObject);
                                if (receivedObject.Type == MessageType.UserList)
                                {
                                    List<string> users = receivedObject.Content.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                                    UserListReceived?.Invoke(users);
                                }
                            }
                            else
                            {
                                Debug.WriteLine($"Клієнт ({_clientNickname}): Не вдалося десеріалізувати JSON в ChatMessage. JSON: {decryptedJson}");
                            }
                        }
                        catch (JsonSerializationException ex)
                        {
                            Debug.WriteLine($"Клієнт ({_clientNickname}): Помилка десеріалізації JSON: {ex.Message}. Зашифроване: '{fullEncryptedMessage.Substring(0, Math.Min(fullEncryptedMessage.Length, 100))}'... Розшифрований (спроба): '{TryDecrypt(fullEncryptedMessage)}'");
                        }
                        catch (FormatException ex)
                        {
                            Debug.WriteLine($"Клієнт ({_clientNickname}): Помилка формату (можливо Base64) при дешифруванні: {ex.Message}. Зашифроване: '{fullEncryptedMessage.Substring(0, Math.Min(fullEncryptedMessage.Length, 100))}'...");
                        }
                        catch (CryptographicException ex)
                        {
                            Debug.WriteLine($"Клієнт ({_clientNickname}): Помилка криптографії при дешифруванні: {ex.Message}. Зашифроване: '{fullEncryptedMessage.Substring(0, Math.Min(fullEncryptedMessage.Length, 100))}'...");
                        }
                        catch (Exception ex_inner_loop)
                        {
                            Debug.WriteLine($"Клієнт ({_clientNickname}): Неочікувана помилка при обробці повідомлення: {ex_inner_loop.Message}. Повідомлення пропущено.");
                        }
                    }
                }
            }
            catch (Exception ex_outer_receive_loop)
            {
                Debug.WriteLine($"Клієнт ({_clientNickname}): Зовнішня помилка в циклі отримання ReceiveAsync: {ex_outer_receive_loop.Message}\nСТЕК: {ex_outer_receive_loop.StackTrace}");
                throw;
            }
            finally
            {
                // Debug.WriteLine($"Клієнт ({_clientNickname}): Блок finally в ReceiveAsync. Поточний стан IsConnected: {IsConnected}");
                if (IsConnected)
                {
                    Debug.WriteLine($"Клієнт ({_clientNickname}): Цикл отримання завершено, але IsConnected=true. Виклик SafeDisconnectAsync з finally.");
                    await SafeDisconnectAsync();
                }
                else
                {
                    ConnectionStatusChanged?.Invoke(false); // Щоб UI знав, що відключено
                }
                Debug.WriteLine($"Клієнт ({_clientNickname}): Роботу ReceiveAsync завершено.");
            }
        }
        private string TryDecrypt(string encryptedText) // Допоміжний метод для логування
        {
            try { return EncryptionHelper.Decrypt(encryptedText); }
            catch { return "[не вдалося розшифрувати]"; }
        }

        // ... (DisconnectAsync, SafeDisconnectAsync, CloseClientResources - без змін відносно попередньої версії) ...
        public async Task DisconnectAsync()
        {
            Debug.WriteLine($"Клієнт ({_clientNickname}): ViewModel ініціював відключення. Поточний стан IsConnected: {IsConnected}");
            if (!IsConnected && _client == null)
            {
                Debug.WriteLine($"Клієнт ({_clientNickname}): Вже відключено, додаткові дії не потрібні.");
                ConnectionStatusChanged?.Invoke(false);
                return;
            }

            if (IsConnected && _stream != null && _stream.CanWrite)
            {
                try
                {
                    var disconnectMessage = new ChatMessage
                    {
                        Type = MessageType.Disconnect,
                        Sender = _clientNickname
                    };
                    string jsonDisconnectMessage = disconnectMessage.ToJson();
                    string encryptedDisconnectMessage = EncryptionHelper.Encrypt(jsonDisconnectMessage);
                    byte[] disconnectData = Encoding.UTF8.GetBytes(encryptedDisconnectMessage + "\n");

                    await _stream.WriteAsync(disconnectData, 0, disconnectData.Length);
                    await _stream.FlushAsync();
                    Debug.WriteLine($"Клієнт ({_clientNickname}): Повідомлення про відключення надіслано на сервер.");
                }
                catch (Exception ex) when (ex is System.IO.IOException || ex is SocketException || ex is ObjectDisposedException)
                {
                    Debug.WriteLine($"Клієнт ({_clientNickname}): Помилка (IO/Socket/Disposed) при надсиланні повідомлення про відключення: {ex.Message}. Ресурси будуть закриті примусово.");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Клієнт ({_clientNickname}): Загальна помилка при надсиланні повідомлення про відключення: {ex.Message}.");
                }
            }
            await CloseClientResources();
        }

        private async Task SafeDisconnectAsync()
        {
            // Debug.WriteLine($"Клієнт ({_clientNickname}): Виклик SafeDisconnectAsync. Поточний стан IsConnected: {IsConnected}");
            if (!IsConnected && _client == null)
            {
                //  Debug.WriteLine($"Клієнт ({_clientNickname}): SafeDisconnectAsync: вже відключено.");
                ConnectionStatusChanged?.Invoke(false);
                return;
            }
            await CloseClientResources();
        }

        private async Task CloseClientResources()
        {
            // Debug.WriteLine($"Клієнт ({_clientNickname}): Закриття ресурсів клієнта...");
            if (_client == null && _stream == null)
            {
                //  Debug.WriteLine($"Клієнт ({_clientNickname}): Ресурси вже були закриті або не ініціалізовані.");
                ConnectionStatusChanged?.Invoke(false);
                return;
            }

            NetworkStream tempStream = _stream;
            TcpClient tempClient = _client;

            _stream = null;
            _client = null;

            try
            {
                tempStream?.Close();
                tempStream?.Dispose();
                // Debug.WriteLine($"Клієнт ({_clientNickname}): Мережевий потік закрито.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Клієнт ({_clientNickname}): Помилка при закритті мережевого потоку: {ex.Message}");
            }

            try
            {
                tempClient?.Close();
                tempClient?.Dispose();
                // Debug.WriteLine($"Клієнт ({_clientNickname}): TCP клієнт закрито.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Клієнт ({_clientNickname}): Помилка при закритті TCP клієнта: {ex.Message}");
            }

            ConnectionStatusChanged?.Invoke(false);
            Debug.WriteLine($"Клієнт ({_clientNickname}): Ресурси клієнта закрито, статус оновлено на 'відключено'.");
        }

        [Obsolete("Використовуйте DisconnectAsync або SafeDisconnectAsync безпосередньо.")]
        public void Disconnect()
        {
            Debug.WriteLine($"Клієнт ({_clientNickname}): Застарілий метод Disconnect() викликано. Перенаправлення на DisconnectAsync.");
            Task.Run(async () => await DisconnectAsync());
        }
    }
}