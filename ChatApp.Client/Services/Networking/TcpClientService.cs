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

namespace ChatApp.Client.Services.Networking
{
    public class TcpClientService
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private string _serverIp;
        private int _serverPort;
        private string _clientNickname; // Додамо для логування та можливого використання

        public event Action<ChatMessage> MessageReceived;
        public event Action<bool> ConnectionStatusChanged;
        public event Action<List<string>> UserListReceived;

        public bool IsConnected => _client?.Connected ?? false;

        public async Task ConnectAsync(string serverIp, int serverPort, string nickname)
        {
            _serverIp = serverIp;
            _serverPort = serverPort;
            _clientNickname = nickname; // Зберігаємо нікнейм

            // Перевірка, чи ми вже не підключені або в процесі
            if (IsConnected)
            {
                Console.WriteLine($"Клієнт ({_clientNickname}): Спроба підключення, коли вже підключено.");
                ConnectionStatusChanged?.Invoke(true); // Повідомити, що вже підключено
                return;
            }
            if (_client != null && !_client.Connected) // Якщо об'єкт є, але не підключений, перестворимо
            {
                _client.Dispose();
                _client = null;
            }


            Console.WriteLine($"Клієнт ({_clientNickname}): Спроба підключення до {serverIp}:{serverPort}...");
            try
            {
                _client = new TcpClient();
                // Встановлення таймаутів може бути корисним
                // _client.SendTimeout = 5000;
                // _client.ReceiveTimeout = 5000; // Обережно з ReceiveTimeout у асинхронному циклі

                await _client.ConnectAsync(_serverIp, _serverPort);
                _stream = _client.GetStream();
                Console.WriteLine($"Клієнт ({_clientNickname}): Успішно підключено до сокета.");

                var connectMessage = new ChatMessage
                {
                    Type = MessageType.SystemMessage,
                    Sender = nickname,
                    Content = "Підключення клієнта..." // Змінено для ясності
                };
                string jsonConnectMessage = connectMessage.ToJson();
                string encryptedData = EncryptionHelper.Encrypt(jsonConnectMessage);
                byte[] data = Encoding.UTF8.GetBytes(encryptedData + "\n");

                await _stream.WriteAsync(data, 0, data.Length);
                await _stream.FlushAsync(); // Переконатися, що дані відправлено
                Console.WriteLine($"Клієнт ({_clientNickname}): Повідомлення про підключення надіслано.");

                ConnectionStatusChanged?.Invoke(true);
                StartReceiving(); // Запускаємо слухання відповідей
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"Клієнт ({_clientNickname}): Помилка сокета при підключенні: {ex.Message} (Код: {ex.SocketErrorCode})");
                ConnectionStatusChanged?.Invoke(false);
                await SafeDisconnectAsync(); // Безпечне відключення
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Клієнт ({_clientNickname}): Неочікувана помилка при підключенні: {ex.Message}");
                ConnectionStatusChanged?.Invoke(false);
                await SafeDisconnectAsync(); // Безпечне відключення
            }
        }

        public async Task SendMessageObjectAsync(ChatMessage messageObject)
        {
            if (!IsConnected || _stream == null)
            {
                Console.WriteLine($"Клієнт ({_clientNickname}): Спроба надіслати повідомлення без активного з'єднання.");
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
                Console.WriteLine($"Клієнт ({_clientNickname}): Помилка надсилання об'єкта повідомлення: {ex.Message}");
                // Можливо, тут варто викликати відключення, якщо помилка серйозна (наприклад, IOException)
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
                Sender = _clientNickname // Клієнт може встановлювати свого відправника, але сервер його все одно перезапише
            };
            await SendMessageObjectAsync(chatMessage);
        }

        private void StartReceiving()
        {
            Console.WriteLine($"Клієнт ({_clientNickname}): Запуск потоку отримання повідомлень.");
            Task.Run(async () => await ReceiveAsync()).ContinueWith(t =>
            {
                if (t.IsFaulted && t.Exception != null)
                {
                    var baseException = t.Exception.GetBaseException();
                    Console.WriteLine($"КЛІЄНТ ({_clientNickname}): КРИТИЧНА ПОМИЛКА В ПОТОЦІ ОТРИМАННЯ: {baseException.Message}\nСТЕК: {baseException.StackTrace}");

                    // Спробувати повідомити UI про помилку, якщо це можливо і безпечно
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        // Повідомляємо ViewModel, що з'єднання втрачено
                        ConnectionStatusChanged?.Invoke(false);
                        // Тут можна було б показати MessageBox, але обробник в App.xaml.cs вже це зробить для необроблених виключень UI
                        // Якщо ця помилка не призвела до краху UI, але з'єднання втрачено, це сповістить користувача.
                    });
                    // Спробувати безпечно закрити ресурси, якщо вони ще не закриті
                    // Важливо: цей код виконується у фоновому потоці ContinueWith
                    Task.Run(async () => await SafeDisconnectAsync());
                }
                else if (t.IsCanceled)
                {
                    Console.WriteLine($"КЛІЄНТ ({_clientNickname}): Потік отримання було скасовано.");
                }
                else
                {
                    Console.WriteLine($"КЛІЄНТ ({_clientNickname}): Потік отримання завершив роботу штатно.");
                }
            });
        }

        private async Task ReceiveAsync()
        {
            try
            {
                byte[] buffer = new byte[8192]; // Збільшений буфер
                StringBuilder receivedDataBuilder = new StringBuilder();
                Console.WriteLine($"Клієнт ({_clientNickname}): Потік ReceiveAsync розпочато, очікування даних...");

                while (_client != null && _client.Connected && _stream != null && _stream.CanRead)
                {
                    int bytesRead = 0;
                    try
                    {
                        // Додаємо CancellationToken для можливості переривання, якщо потрібно
                        // CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)); // Таймаут на читання
                        // bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                        bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
                    }
                    catch (System.IO.IOException ex) // Може виникнути, якщо сокет закрито з іншого боку або мережева помилка
                    {
                        Console.WriteLine($"Клієнт ({_clientNickname}): IOException під час читання з потоку: {ex.Message}. З'єднання буде закрито.");
                        break; // Вихід з циклу для закриття з'єднання
                    }
                    catch (ObjectDisposedException ex) // Якщо потік або клієнт вже знищено
                    {
                        Console.WriteLine($"Клієнт ({_clientNickname}): ObjectDisposedException під час читання з потоку: {ex.Message}. З'єднання буде закрито.");
                        break;
                    }


                    if (bytesRead == 0) // Сервер коректно закрив з'єднання або з'єднання втрачено
                    {
                        Console.WriteLine($"Клієнт ({_clientNickname}): Сервер закрив з'єднання або з'єднання втрачено (bytesRead == 0).");
                        break; // Вихід з циклу
                    }

                    receivedDataBuilder.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
                    string currentBufferContent = receivedDataBuilder.ToString();
                    int newlineIndex;

                    while ((newlineIndex = currentBufferContent.IndexOf('\n')) != -1)
                    {
                        string fullEncryptedMessage = currentBufferContent.Substring(0, newlineIndex);
                        receivedDataBuilder.Remove(0, newlineIndex + 1);
                        currentBufferContent = receivedDataBuilder.ToString(); // Оновлюємо буфер для наступної ітерації while

                        if (string.IsNullOrWhiteSpace(fullEncryptedMessage)) continue; // Пропускаємо порожні повідомлення

                        // Console.WriteLine($"Клієнт ({_clientNickname}): Отримано зашифрований блок: {fullEncryptedMessage.Length} байт.");
                        try
                        {
                            string decryptedJson = EncryptionHelper.Decrypt(fullEncryptedMessage);
                            // Console.WriteLine($"Клієнт ({_clientNickname}): Розшифровано JSON: {decryptedJson}");
                            ChatMessage receivedObject = ChatMessage.FromJson(decryptedJson);

                            if (receivedObject != null)
                            {
                                // Безпосередній виклик події для ViewModel
                                MessageReceived?.Invoke(receivedObject);

                                // Обробка UserList тут залишається, оскільки це специфічна логіка сервісу
                                if (receivedObject.Type == MessageType.UserList)
                                {
                                    List<string> users = receivedObject.Content.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                                    UserListReceived?.Invoke(users);
                                }
                            }
                            else
                            {
                                Console.WriteLine($"Клієнт ({_clientNickname}): Не вдалося десеріалізувати JSON в ChatMessage. JSON: {decryptedJson}");
                            }
                        }
                        catch (JsonSerializationException ex)
                        {
                            Console.WriteLine($"Клієнт ({_clientNickname}): Помилка десеріалізації JSON: {ex.Message}. Зашифроване: '{fullEncryptedMessage}'");
                        }
                        catch (FormatException ex) // Помилка Base64 при дешифруванні
                        {
                            Console.WriteLine($"Клієнт ({_clientNickname}): Помилка формату (можливо Base64) при дешифруванні: {ex.Message}. Зашифроване: '{fullEncryptedMessage}'");
                        }
                        catch (CryptographicException ex)
                        {
                            Console.WriteLine($"Клієнт ({_clientNickname}): Помилка криптографії при дешифруванні: {ex.Message}. Зашифроване: '{fullEncryptedMessage}'");
                        }
                        catch (Exception ex_inner_loop) // Інші непередбачені помилки обробки повідомлення
                        {
                            Console.WriteLine($"Клієнт ({_clientNickname}): Неочікувана помилка при обробці повідомлення: {ex_inner_loop.Message}. Повідомлення пропущено.");
                        }
                    }
                }
            }
            // Не ловимо IOException тут, якщо він уже оброблений вище і призвів до break.
            // ObjectDisposedException також обробляється вище.
            catch (Exception ex_outer_receive_loop) // Ловимо будь-які інші помилки, що виникли поза внутрішнім try-catch або ReadAsync
            {
                Console.WriteLine($"Клієнт ({_clientNickname}): Зовнішня помилка в циклі отримання ReceiveAsync: {ex_outer_receive_loop.Message}\nСТЕК: {ex_outer_receive_loop.StackTrace}");
                // Ця помилка може бути причиною краху, якщо її не обробить ContinueWith
                throw; // Перекидаємо помилку, щоб її зловив ContinueWith у StartReceiving
            }
            finally
            {
                Console.WriteLine($"Клієнт ({_clientNickname}): Блок finally в ReceiveAsync. Поточний стан IsConnected: {IsConnected}");
                // Важливо: цей блок може викликатися кілька разів, якщо помилки йдуть одна за одною.
                // Потрібно забезпечити, щоб DisconnectAsync був ідемпотентним або викликався лише один раз.
                if (IsConnected) // Якщо ми все ще вважаємо, що підключені, але цикл завершився
                {
                    Console.WriteLine($"Клієнт ({_clientNickname}): Цикл отримання завершено, але IsConnected=true. Виклик SafeDisconnectAsync з finally.");
                    await SafeDisconnectAsync(); // Викликаємо безпечне відключення
                }
                else
                {
                    // Якщо вже відключено, то просто сповіщаємо, якщо це ще не зроблено
                    ConnectionStatusChanged?.Invoke(false);
                }
                Console.WriteLine($"Клієнт ({_clientNickname}): Роботу ReceiveAsync завершено.");
            }
        }

        public async Task DisconnectAsync() // Цей метод викликається з ViewModel
        {
            Console.WriteLine($"Клієнт ({_clientNickname}): ViewModel ініціював відключення. Поточний стан IsConnected: {IsConnected}");
            if (!IsConnected && _client == null) // Якщо вже відключено і об'єктів немає
            {
                Console.WriteLine($"Клієнт ({_clientNickname}): Вже відключено, додаткові дії не потрібні.");
                ConnectionStatusChanged?.Invoke(false); // Просто підтвердити статус
                return;
            }

            // Надсилаємо повідомлення про відключення, тільки якщо є активний потік
            if (IsConnected && _stream != null && _stream.CanWrite)
            {
                try
                {
                    var disconnectMessage = new ChatMessage
                    {
                        Type = MessageType.Disconnect,
                        Sender = _clientNickname // Надсилаємо нікнейм для ідентифікації на сервері
                    };
                    string jsonDisconnectMessage = disconnectMessage.ToJson();
                    string encryptedDisconnectMessage = EncryptionHelper.Encrypt(jsonDisconnectMessage);
                    byte[] disconnectData = Encoding.UTF8.GetBytes(encryptedDisconnectMessage + "\n");

                    await _stream.WriteAsync(disconnectData, 0, disconnectData.Length);
                    await _stream.FlushAsync();
                    Console.WriteLine($"Клієнт ({_clientNickname}): Повідомлення про відключення надіслано на сервер.");
                }
                catch (Exception ex) when (ex is System.IO.IOException || ex is SocketException || ex is ObjectDisposedException)
                {
                    Console.WriteLine($"Клієнт ({_clientNickname}): Помилка (IO/Socket/Disposed) при надсиланні повідомлення про відключення: {ex.Message}. Ресурси будуть закриті примусово.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Клієнт ({_clientNickname}): Загальна помилка при надсиланні повідомлення про відключення: {ex.Message}.");
                }
            }
            await CloseClientResources(); // Закриваємо ресурси незалежно від успіху надсилання
        }

        // Внутрішній метод для безпечного закриття ресурсів
        private async Task SafeDisconnectAsync()
        {
            Console.WriteLine($"Клієнт ({_clientNickname}): Виклик SafeDisconnectAsync. Поточний стан IsConnected: {IsConnected}");
            if (!IsConnected && _client == null) // Додаткова перевірка
            {
                Console.WriteLine($"Клієнт ({_clientNickname}): SafeDisconnectAsync: вже відключено.");
                ConnectionStatusChanged?.Invoke(false);
                return;
            }
            await CloseClientResources();
        }

        private async Task CloseClientResources()
        {
            Console.WriteLine($"Клієнт ({_clientNickname}): Закриття ресурсів клієнта...");
            // Запобігання подвійному виконанню, якщо _client вже null
            if (_client == null && _stream == null)
            {
                Console.WriteLine($"Клієнт ({_clientNickname}): Ресурси вже були закриті або не ініціалізовані.");
                ConnectionStatusChanged?.Invoke(false); // Впевнитись, що статус оновлено
                return;
            }

            NetworkStream tempStream = _stream;
            TcpClient tempClient = _client;

            _stream = null; // Очищуємо посилання, щоб запобігти подальшому використанню
            _client = null; // з інших потоків, поки ми тут закриваємо

            try
            {
                tempStream?.Close(); // Закриваємо потік
                tempStream?.Dispose();
                Console.WriteLine($"Клієнт ({_clientNickname}): Мережевий потік закрито.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Клієнт ({_clientNickname}): Помилка при закритті мережевого потоку: {ex.Message}");
            }

            try
            {
                tempClient?.Close(); // Закриваємо TCP клієнт
                tempClient?.Dispose();
                Console.WriteLine($"Клієнт ({_clientNickname}): TCP клієнт закрито.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Клієнт ({_clientNickname}): Помилка при закритті TCP клієнта: {ex.Message}");
            }

            // Сповіщаємо про зміну статусу після фактичного закриття ресурсів
            ConnectionStatusChanged?.Invoke(false);
            Console.WriteLine($"Клієнт ({_clientNickname}): Ресурси клієнта закрито, статус оновлено на 'відключено'.");
        }


        // Цей метод викликається з MainWindowViewModel.Disconnect() або з finally блоків
        // Якщо він є, він має бути безпечним
        [Obsolete("Використовуйте DisconnectAsync або SafeDisconnectAsync безпосередньо.")]
        public void Disconnect()
        {
            Console.WriteLine($"Клієнт ({_clientNickname}): Застарілий метод Disconnect() викликано. Перенаправлення на DisconnectAsync.");
            Task.Run(async () => await DisconnectAsync()); // Запускаємо асинхронний метод
        }
    }
}