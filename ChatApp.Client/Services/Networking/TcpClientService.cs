using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using ChatApp.Common.Utilities;
using System.Security.Cryptography;
using ChatApp.Common.Models; // Додано для ChatMessage
using Newtonsoft.Json;

namespace ChatApp.Client.Services.Networking
{
    public class TcpClientService
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private string _serverIp;
        private int _serverPort;

        public event Action<string> MessageReceived;
        public event Action<bool> ConnectionStatusChanged;
        public event Action<List<string>> UserListReceived;

        public bool IsConnected => _client?.Connected ?? false;

        public async Task ConnectAsync(string serverIp, int serverPort, string nickname)
        {
            _serverIp = serverIp;
            _serverPort = serverPort;

            try
            {
                _client = new TcpClient();
                await _client.ConnectAsync(_serverIp, _serverPort);
                _stream = _client.GetStream();

                // Створюємо об'єкт ChatMessage для нікнейму (як системне повідомлення)
                var connectMessage = new ChatMessage
                {
                    Type = MessageType.SystemMessage,
                    Sender = nickname,
                    Content = "Підключення..."
                };
                string jsonConnectMessage = connectMessage.ToJson();

                // Шифруємо JSON, а потім додаємо \n
                string encryptedData = EncryptionHelper.Encrypt(jsonConnectMessage);
                byte[] data = Encoding.UTF8.GetBytes(encryptedData + "\n");
                await _stream.WriteAsync(data, 0, data.Length);

                ConnectionStatusChanged?.Invoke(true);
                StartReceiving();
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"Клієнт: Помилка підключення: {ex.Message}");
                ConnectionStatusChanged?.Invoke(false);
                Disconnect();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Клієнт: Неочікувана помилка підключення: {ex.Message}");
                ConnectionStatusChanged?.Invoke(false);
                Disconnect();
            }
        }

        public async Task SendMessageObjectAsync(ChatMessage messageObject) // Новий метод для надсилання об'єктів
        {
            if (_stream != null && _client.Connected)
            {
                string jsonMessage = messageObject.ToJson();
                string encryptedMessage = EncryptionHelper.Encrypt(jsonMessage);
                byte[] data = Encoding.UTF8.GetBytes(encryptedMessage + "\n");
                await _stream.WriteAsync(data, 0, data.Length);
            }
        }

        public async Task SendMessageAsync(string messageContent) // Змінено на об'єкт ChatMessage
        {
            var chatMessage = new ChatMessage
            {
                Type = MessageType.ChatMessage,
                Content = messageContent
            };
            await SendMessageObjectAsync(chatMessage);
        }

        private async Task ReceiveAsync()
        {
            try
            {
                byte[] buffer = new byte[4096];
                StringBuilder receivedDataBuilder = new StringBuilder();

                while (_client != null && _client.Connected)
                {
                    int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) // З'єднання закрито
                    {
                        break;
                    }

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
                            string decryptedJson = EncryptionHelper.Decrypt(fullEncryptedMessage); // Розшифровуємо
                            ChatMessage receivedObject = ChatMessage.FromJson(decryptedJson);

                            if (receivedObject != null)
                            {
                                switch (receivedObject.Type)
                                {
                                    case MessageType.ChatMessage:
                                    case MessageType.SystemMessage:
                                        // Для звичайних та системних повідомлень, відображаємо Content
                                        // Сервер вже додає нікнейм, тому тут просто відображаємо Content
                                        MessageReceived?.Invoke($"[{receivedObject.Timestamp.ToString("HH:mm:ss")}] {receivedObject.Sender}: {receivedObject.Content}");
                                        break;
                                    case MessageType.UserList:
                                        // Обробляємо список користувачів
                                        List<string> users = receivedObject.Content.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                                        UserListReceived?.Invoke(users);
                                        break;
                                    case MessageType.Disconnect:
                                        // Обробка відключення, якщо це системний сигнал
                                        Console.WriteLine("Клієнт: Отримано сигнал відключення від сервера.");
                                        break;
                                    case MessageType.FileTransferMetadata:
                                        Console.WriteLine($"Клієнт: Отримано метадані файлу '{receivedObject.FileName}' ({receivedObject.FileSize} байт) від {receivedObject.Sender}.");
                                        MessageReceived?.Invoke($"[{receivedObject.Timestamp.ToString("HH:mm:ss")}] {receivedObject.Sender} хоче надіслати файл: {receivedObject.FileName} ({receivedObject.FileSize / 1024} KB).");
                                        // Тут буде логіка початку отримання файлу (пізніше)
                                        break;
                                    case MessageType.FileTransferChunk:
                                        // Console.WriteLine($"Клієнт: Отримано фрагмент {receivedObject.ChunkIndex}/{receivedObject.TotalChunks} для файлу {receivedObject.FileId}.");
                                        // Тут буде логіка збору фрагментів (пізніше)
                                        break;
                                    case MessageType.FileTransferEnd:
                                        Console.WriteLine($"Клієнт: Отримано кінець файлу '{receivedObject.FileName}' від {receivedObject.Sender}.");
                                        MessageReceived?.Invoke($"[{receivedObject.Timestamp.ToString("HH:mm:ss")}] {receivedObject.Sender} надіслав файл: {receivedObject.FileName}.");
                                        // Тут буде логіка завершення отримання файлу (пізніше)
                                        break;
                                    default:
                                        Console.WriteLine($"Клієнт: Отримано невідомий тип повідомлення: {receivedObject.Type}");
                                        break;
                                }
                            }
                        }
                        catch (JsonSerializationException ex)
                        {
                            Console.WriteLine($"Клієнт: Помилка десеріалізації JSON: {ex.Message}");
                        }
                        catch (FormatException ex)
                        {
                            Console.WriteLine($"Клієнт: Помилка формату Base64 при розшифруванні. Можливо, неповні дані. {ex.Message}");
                        }
                        catch (CryptographicException ex)
                        {
                            Console.WriteLine($"Клієнт: Помилка криптографії при розшифруванні. Можливо, невірний ключ/IV або пошкодження. {ex.Message}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Клієнт: Неочікувана помилка при розшифруванні або обробці: {ex.Message}");
                        }
                    }
                }
            }
            catch (System.IO.IOException ex)
            {
                Console.WriteLine($"Клієнт: З'єднання було розірвано або виникла помилка вводу/виводу. {ex.Message}");
                ConnectionStatusChanged?.Invoke(false);
                Disconnect();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Клієнт: Помилка отримання даних: {ex.Message}");
                ConnectionStatusChanged?.Invoke(false);
                Disconnect();
            }
            finally
            {
                Disconnect();
            }
        }

        private void StartReceiving()
        {
            Task.Run(() => ReceiveAsync());
        }

        public async Task DisconnectAsync()
        {
            if (_stream != null && _client.Connected)
            {
                var disconnectMessage = new ChatMessage { Type = MessageType.Disconnect };
                string jsonDisconnectMessage = disconnectMessage.ToJson();

                string encryptedDisconnectMessage = EncryptionHelper.Encrypt(jsonDisconnectMessage);
                byte[] disconnectData = Encoding.UTF8.GetBytes(encryptedDisconnectMessage + "\n");
                try
                {
                    await _stream.WriteAsync(disconnectData, 0, disconnectData.Length);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Клієнт: Помилка надсилання запиту на відключення: {ex.Message}");
                }
                await Task.Delay(100);
                _stream.Close();
            }
            if (_client != null)
            {
                _client.Close();
            }
            ConnectionStatusChanged?.Invoke(false);
        }

        public void Disconnect()
        {
            Task.Run(DisconnectAsync);
        }
    }
}