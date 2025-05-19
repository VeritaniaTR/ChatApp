using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using ChatApp.Client.Models;
using ChatApp.Client.Services.Networking;
using System.Collections.Generic;
using ChatApp.Common.Models;
using Microsoft.Win32;
using System.IO;
using MimeMapping; // Переконайтесь, що цей пакет встановлено (MimeMapping Nuget package)
using System.Linq;
using System.Windows.Media.Imaging;

namespace ChatApp.Client.ViewModels
{
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        private string _serverIp = "127.0.0.1";
        private int _serverPort = 12345;
        private string _messageToSend;
        private string _connectionStatus = "Не підключено";
        private bool _isConnected = false;
        private TcpClientService _tcpClientService;
        private string _nickname;

        private double _fileTransferProgress;
        private bool _isFileTransferring;
        // ЗМІНЕНО: Ключ словника - Guid FileId. Значення - тимчасовий об'єкт для збору файлу.
        private Dictionary<Guid, FileReceptionState> _receivedFiles = new Dictionary<Guid, FileReceptionState>();

        public ObservableCollection<Message> ChatMessages { get; } = new ObservableCollection<Message>();
        public ObservableCollection<string> OnlineUsers { get; } = new ObservableCollection<string>();

        public string ServerIp
        {
            get => _serverIp;
            set { _serverIp = value; OnPropertyChanged(); }
        }

        public int ServerPort
        {
            get => _serverPort;
            set { _serverPort = value; OnPropertyChanged(); }
        }

        public string MessageToSend
        {
            get => _messageToSend;
            set { _messageToSend = value; OnPropertyChanged(); }
        }

        public string ConnectionStatus
        {
            get => _connectionStatus;
            set { _connectionStatus = value; OnPropertyChanged(); }
        }

        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                _isConnected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ConnectionStatusColor));
                // Оновлення стану кнопок
                ((RelayCommand)ConnectCommand).RaiseCanExecuteChanged();
                ((RelayCommand)SendCommand).RaiseCanExecuteChanged();
                ((RelayCommand)DisconnectCommand).RaiseCanExecuteChanged();
                ((RelayCommand)SendFileCommand).RaiseCanExecuteChanged();
            }
        }

        public string Nickname
        {
            get => _nickname;
            set { _nickname = value; OnPropertyChanged(); }
        }

        public Brush ConnectionStatusColor => IsConnected ? Brushes.Green : Brushes.Red;

        public double FileTransferProgress
        {
            get => _fileTransferProgress;
            set { _fileTransferProgress = value; OnPropertyChanged(); }
        }

        public bool IsFileTransferring
        {
            get => _isFileTransferring;
            set { _isFileTransferring = value; OnPropertyChanged(); }
        }

        public ICommand ConnectCommand { get; }
        public ICommand SendCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand SendFileCommand { get; }

        public MainWindowViewModel()
        {
            _tcpClientService = new TcpClientService();
            // ЗМІНЕНО: Тип параметра в OnMessageReceived тепер ChatMessage
            _tcpClientService.MessageReceived += OnMessageReceived;
            _tcpClientService.ConnectionStatusChanged += OnConnectionStatusChanged;
            _tcpClientService.UserListReceived += OnUserListReceived;

            ConnectCommand = new RelayCommand(async (o) => await ConnectToServerAsync(), (o) => !IsConnected && !string.IsNullOrWhiteSpace(Nickname));
            SendCommand = new RelayCommand(async (o) => await SendMessageAsync(), (o) => IsConnected && !string.IsNullOrEmpty(MessageToSend));
            DisconnectCommand = new RelayCommand(async (o) => await DisconnectFromServerAsync(), (o) => IsConnected);
            SendFileCommand = new RelayCommand(async (o) => await SendFileAsync(), (o) => IsConnected && !IsFileTransferring);
        }

        private async Task ConnectToServerAsync()
        {
            // IsConnected перевіряється в CanExecute
            ConnectionStatus = $"Підключення до {_serverIp}:{_serverPort} як {Nickname}...";
            ChatMessages.Clear();
            OnlineUsers.Clear();
            await _tcpClientService.ConnectAsync(_serverIp, _serverPort, Nickname);
        }

        private async Task SendMessageAsync()
        {
            // IsConnected та MessageToSend перевіряються в CanExecute
            var messageToActuallySend = _messageToSend; // Зберігаємо перед очищенням
            MessageToSend = string.Empty; // Очищуємо поле вводу одразу

            await _tcpClientService.SendMessageAsync(messageToActuallySend);
            // Додаємо своє повідомлення до чату локально. Sender буде доданий сервером для інших.
            // Або, якщо хочемо бачити "Ви:", робимо це тут.
            // Сервер НЕ буде надсилати це повідомлення назад відправнику, якщо логіка BroadcastMessageAsync правильна.
            ChatMessages.Add(new Message { Text = $"Ви: {messageToActuallySend}", Timestamp = DateTime.Now, Sender = Nickname, IsOwnMessage = true });

        }

        private async Task SendFileAsync()
        {
            // IsConnected перевіряється в CanExecute
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "All files (*.*)|*.*|Images (*.jpg;*.jpeg;*.png;*.gif)|*.jpg;*.jpeg;*.png;*.gif|Text files (*.txt)|*.txt",
                Title = "Оберіть файл для надсилання"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string filePath = openFileDialog.FileName;
                string fileName = Path.GetFileName(filePath);
                string fileMimeType = MimeUtility.GetMimeMapping(fileName); // MimeMapping NuGet
                long fileSize = new FileInfo(filePath).Length;
                const long MaxFileSize = 50 * 1024 * 1024; // 50 MB

                if (fileSize > MaxFileSize)
                {
                    AddSystemMessageToChat($"Файл занадто великий. Макс. розмір: {MaxFileSize / (1024 * 1024)} MB.");
                    return;
                }

                byte[] fileBytes;
                try
                {
                    fileBytes = File.ReadAllBytes(filePath);
                }
                catch (Exception ex)
                {
                    AddSystemMessageToChat($"Помилка читання файлу: {ex.Message}");
                    return;
                }

                Guid fileId = Guid.NewGuid();
                IsFileTransferring = true;
                FileTransferProgress = 0;
                ((RelayCommand)SendFileCommand).RaiseCanExecuteChanged(); // Оновити стан кнопки

                var metadataMessage = new ChatMessage
                {
                    Type = MessageType.FileTransferMetadata,
                    Sender = Nickname,
                    FileId = fileId,
                    FileName = fileName,
                    FileSize = fileSize,
                    FileMimeType = fileMimeType,
                    Content = $"Надсилання файлу: {fileName} ({FormatFileSize(fileSize)})"
                };
                await _tcpClientService.SendMessageObjectAsync(metadataMessage);
                AddSystemMessageToChat($"Розпочато надсилання файлу: {fileName}");

                int chunkSize = 1024 * 64; // 64 KB
                long totalChunks = (long)Math.Ceiling((double)fileSize / chunkSize);

                for (int i = 0; i < totalChunks; i++)
                {
                    if (!IsConnected) // Перевірка з'єднання перед надсиланням кожного чанку
                    {
                        AddSystemMessageToChat("З'єднання втрачено під час надсилання файлу.");
                        IsFileTransferring = false;
                        ((RelayCommand)SendFileCommand).RaiseCanExecuteChanged();
                        return;
                    }
                    int offset = i * chunkSize;
                    int length = Math.Min(chunkSize, (int)(fileSize - offset));
                    byte[] chunkBytes = new byte[length];
                    Buffer.BlockCopy(fileBytes, offset, chunkBytes, 0, length);

                    var chunkMessage = new ChatMessage
                    {
                        Type = MessageType.FileTransferChunk,
                        Sender = Nickname,
                        FileId = fileId,
                        ChunkIndex = i,
                        TotalChunks = (int)totalChunks,
                        FileData = Convert.ToBase64String(chunkBytes) // Дані чанку
                    };
                    await _tcpClientService.SendMessageObjectAsync(chunkMessage);
                    FileTransferProgress = (double)(i + 1) / totalChunks * 100;
                }

                var endMessage = new ChatMessage
                {
                    Type = MessageType.FileTransferEnd,
                    Sender = Nickname,
                    FileId = fileId,
                    FileName = fileName, // Додано FileName для консистентності
                    Content = $"Файл '{fileName}' надіслано."
                };
                await _tcpClientService.SendMessageObjectAsync(endMessage);

                IsFileTransferring = false;
                FileTransferProgress = 100; // Або 0 після завершення
                ((RelayCommand)SendFileCommand).RaiseCanExecuteChanged();
                ChatMessages.Add(new Message { Text = $"Ви надіслали файл: {fileName}", Timestamp = DateTime.Now, Sender = Nickname, IsOwnMessage = true });
            }
        }

        private async Task DisconnectFromServerAsync()
        {
            // IsConnected перевіряється в CanExecute
            await _tcpClientService.DisconnectAsync();
            // ConnectionStatus та IsConnected оновляться через подію OnConnectionStatusChanged
        }

        // ЗМІНЕНО: Параметр тепер ChatMessage
        private void OnMessageReceived(ChatMessage receivedObject)
        {
            // Викликаємо асинхронний обробник
            Task.Run(async () => await OnMessageReceivedAsync(receivedObject));
        }

        private async Task OnMessageReceivedAsync(ChatMessage receivedObject)
        {
            // Перемикання на UI потік для оновлення ObservableCollection
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                switch (receivedObject.Type)
                {
                    case MessageType.ChatMessage:
                        ChatMessages.Add(new Message
                        {
                            Text = $"{receivedObject.Sender}: {receivedObject.Content}",
                            Timestamp = receivedObject.Timestamp,
                            Sender = receivedObject.Sender,
                            IsOwnMessage = receivedObject.Sender == Nickname // Визначаємо, чи це наше повідомлення (якщо сервер не фільтрує)
                        });
                        break;
                    case MessageType.SystemMessage:
                        if (receivedObject.Content == "Нікнейм вже зайнятий, спробуйте інший!")
                        {
                            ConnectionStatus = receivedObject.Content;
                            // Потенційно розірвати з'єднання або змусити користувача змінити нік
                            Task.Run(async () => await _tcpClientService.DisconnectAsync()); // Розриваємо з'єднання
                        }
                        else
                        {
                            ChatMessages.Add(new Message { Text = receivedObject.Content, Timestamp = receivedObject.Timestamp, Sender = "System", IsSystemMessage = true });
                        }
                        break;
                    // case MessageType.UserList: // Обробляється в OnUserListReceived
                    //    break;
                    case MessageType.Disconnect: // Обробляється в OnConnectionStatusChanged або просто ігнорується, якщо це підтвердження
                        // Зазвичай це призведе до спрацювання OnConnectionStatusChanged(false)
                        break;

                    case MessageType.FileTransferMetadata:
                        if (receivedObject.FileId != Guid.Empty && !string.IsNullOrEmpty(receivedObject.FileName))
                        {
                            AddSystemMessageToChat($"[{receivedObject.Timestamp:HH:mm:ss}] {receivedObject.Sender} хоче надіслати файл: {receivedObject.FileName} ({FormatFileSize(receivedObject.FileSize)}).");
                            _receivedFiles[receivedObject.FileId] = new FileReceptionState(receivedObject.FileName, receivedObject.FileSize, receivedObject.FileMimeType, (int)receivedObject.TotalChunks);
                        }
                        break;

                    case MessageType.FileTransferChunk:
                        if (receivedObject.FileId != Guid.Empty &&
                            _receivedFiles.TryGetValue(receivedObject.FileId, out FileReceptionState fileState) &&
                            !string.IsNullOrEmpty(receivedObject.FileData))
                        {
                            try
                            {
                                byte[] chunkData = Convert.FromBase64String(receivedObject.FileData);
                                fileState.AddChunk(receivedObject.ChunkIndex, chunkData);

                                // Оновлення прогресу отримання (якщо потрібно відображати)
                                // double progress = fileState.GetReceptionProgress();
                                // AddSystemMessageToChat($"Отримано чанк {receivedObject.ChunkIndex + 1}/{fileState.TotalChunks} для {fileState.FileName}");

                                if (fileState.IsComplete())
                                {
                                    byte[] assembledFileBytes = fileState.GetAssembledFile();
                                    string downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ChatAppDownloads");
                                    Directory.CreateDirectory(downloadsPath); // Створюємо папку, якщо її немає
                                    string tempFilePath = Path.Combine(downloadsPath, fileState.FileName);

                                    // Запобігання перезапису файлів з однаковими іменами
                                    int count = 1;
                                    string fileNameOnly = Path.GetFileNameWithoutExtension(tempFilePath);
                                    string extension = Path.GetExtension(tempFilePath);
                                    while (File.Exists(tempFilePath))
                                    {
                                        tempFilePath = Path.Combine(downloadsPath, $"{fileNameOnly} ({count++}){extension}");
                                    }

                                    File.WriteAllBytes(tempFilePath, assembledFileBytes);

                                    if (fileState.MimeType.StartsWith("image/"))
                                    {
                                        BitmapImage bitmap = new BitmapImage();
                                        using (var ms = new MemoryStream(assembledFileBytes))
                                        {
                                            bitmap.BeginInit();
                                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                            bitmap.StreamSource = ms;
                                            bitmap.EndInit();
                                            bitmap.Freeze(); // Важливо для використання в іншому потоці (UI)
                                        }
                                        ChatMessages.Add(new Message { Timestamp = receivedObject.Timestamp, Sender = receivedObject.Sender, Image = bitmap, IsImage = true, FilePath = tempFilePath /* Зберігаємо шлях */ });
                                        AddSystemMessageToChat($"Зображення '{fileState.FileName}' отримано від {receivedObject.Sender} та збережено: {tempFilePath}");
                                    }
                                    else
                                    {
                                        ChatMessages.Add(new Message { Text = $"Файл '{fileState.FileName}' отримано від {receivedObject.Sender} та збережено: {tempFilePath}", Timestamp = receivedObject.Timestamp, FilePath = tempFilePath, IsImage = false, Sender = receivedObject.Sender });
                                    }
                                    _receivedFiles.Remove(receivedObject.FileId);
                                }
                            }
                            catch (FormatException ex) { AddSystemMessageToChat($"Помилка Base64 для чанку файлу {fileState.FileName}: {ex.Message}"); }
                            catch (Exception ex) { AddSystemMessageToChat($"Помилка обробки чанку файлу {fileState.FileName}: {ex.Message}"); }
                        }
                        break;

                    case MessageType.FileTransferEnd:
                        if (receivedObject.FileId != Guid.Empty && _receivedFiles.ContainsKey(receivedObject.FileId))
                        {
                            // Якщо файл ще не зібраний (наприклад, не всі чанки прийшли), це повідомлення може бути інформативним.
                            // Зазвичай, FileTransferChunk має зібрати файл.
                            // Можна додати логіку перевірки, чи файл дійсно зібраний.
                            if (!_receivedFiles[receivedObject.FileId].IsComplete())
                            {
                                AddSystemMessageToChat($"Отримано сигнал завершення для файлу '{receivedObject.FileName}', але не всі частини зібрано.");
                                // Можливо, варто видалити незавершений файл із _receivedFiles
                            }
                            else
                            {
                                // Файл вже має бути оброблений логікою FileTransferChunk
                                //AddSystemMessageToChat($"Передачу файлу '{receivedObject.FileName}' від {receivedObject.Sender} завершено.");
                            }
                            // _receivedFiles.Remove(receivedObject.FileId); // Видаляємо, якщо ще не видалено
                        }
                        break;

                    default:
                        // Обробка невідомих типів або UserList, якщо не винесено окремо
                        if (receivedObject.Type != MessageType.UserList) // UserList обробляється в OnUserListReceived
                        {
                            ChatMessages.Add(new Message { Text = $"Невідомий тип: {receivedObject.Sender}: {receivedObject.Content}", Timestamp = receivedObject.Timestamp, Sender = receivedObject.Sender });
                        }
                        break;
                }
            });
        }

        private void OnConnectionStatusChanged(bool isConnected)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => // Переконайтесь, що це в UI потоці
            {
                IsConnected = isConnected;
                ConnectionStatus = isConnected ? $"Підключено як {Nickname}" : "Не підключено";
                if (!isConnected)
                {
                    OnlineUsers.Clear();
                    // ChatMessages.Clear(); // Можливо, не варто очищати чат при дисконекті, щоб користувач бачив історію
                    AddSystemMessageToChat("З'єднання з сервером розірвано.");
                }
                else
                {
                    AddSystemMessageToChat("З'єднано з сервером!");
                }
            });
        }

        private void OnUserListReceived(List<string> users)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                OnlineUsers.Clear();
                users.ForEach(OnlineUsers.Add);
            });
        }

        private void AddSystemMessageToChat(string text)
        {
            // Переконайтеся, що оновлення колекції відбувається в UI-потоці
            System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ChatMessages.Add(new Message { Text = text, Timestamp = DateTime.Now, Sender = "System", IsSystemMessage = true });
            });
        }

        private string FormatFileSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            double dblSByte = bytes;
            while (dblSByte >= 1024 && i < suffixes.Length - 1)
            {
                dblSByte /= 1024;
                i++;
            }
            return $"{dblSByte:0.##} {suffixes[i]}";
        }


        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // Внутрішній клас для відстеження стану отримання файлу
        private class FileReceptionState
        {
            public string FileName { get; }
            public long FileSize { get; }
            public string MimeType { get; }
            public int TotalChunks { get; }
            private readonly List<byte[]> _chunks;
            private int _receivedChunksCount;

            public FileReceptionState(string fileName, long fileSize, string mimeType, int totalChunks)
            {
                FileName = fileName;
                FileSize = fileSize;
                MimeType = mimeType;
                TotalChunks = totalChunks;
                _chunks = new List<byte[]>(new byte[totalChunks][]);
                _receivedChunksCount = 0;
            }

            public void AddChunk(int index, byte[] data)
            {
                if (index >= 0 && index < TotalChunks && _chunks[index] == null)
                {
                    _chunks[index] = data;
                    _receivedChunksCount++;
                }
            }

            public bool IsComplete() => _receivedChunksCount == TotalChunks && _chunks.All(c => c != null);

            public byte[] GetAssembledFile()
            {
                if (!IsComplete()) throw new InvalidOperationException("Файл не повністю отримано.");
                using (var ms = new MemoryStream())
                {
                    for (int i = 0; i < TotalChunks; i++)
                    {
                        ms.Write(_chunks[i], 0, _chunks[i].Length);
                    }
                    return ms.ToArray();
                }
            }
            public double GetReceptionProgress() => TotalChunks == 0 ? 0 : (double)_receivedChunksCount / TotalChunks * 100;
        }
    }


    // Клас RelayCommand (якщо він не визначений в іншому місці глобально)
    // Переконайтесь, що CanExecuteChanged викликається, коли змінюються умови виконання команди
    public class RelayCommand : ICommand
    {
        private readonly Func<object, Task> _executeAsync;
        private readonly Action<object> _executeSync;
        private readonly Predicate<object> _canExecute;
        private readonly bool _isAsync;

        public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
        {
            _executeSync = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
            _isAsync = false;
        }

        public RelayCommand(Func<object, Task> executeAsync, Predicate<object> canExecute = null)
        {
            _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
            _canExecute = canExecute;
            _isAsync = true;
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute(parameter);

        public async void Execute(object parameter)
        {
            if (_isAsync)
            {
                await _executeAsync(parameter);
            }
            else
            {
                _executeSync(parameter);
            }
        }

        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public void RaiseCanExecuteChanged() // Додайте цей метод
        {
            CommandManager.InvalidateRequerySuggested();
        }
    }
}