using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics; // Для Debug.WriteLine
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ChatApp.Client.Models;
using ChatApp.Client.Services.Networking;
using ChatApp.Common.Models;
using Microsoft.Win32;
using MimeMapping;

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
        private Dictionary<Guid, FileReceptionState> _receivedFiles = new Dictionary<Guid, FileReceptionState>();

        public ObservableCollection<Message> ChatMessages { get; } = new ObservableCollection<Message>();
        public ObservableCollection<string> OnlineUsers { get; } = new ObservableCollection<string>();

        // ... (решта властивостей як у вас) ...
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
                if (_isConnected == value) return; // Запобігання зайвим оновленням
                _isConnected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ConnectionStatusColor));
                // Оновлення стану CanExecute для команд
                ((RelayCommand)ConnectCommand).RaiseCanExecuteChanged();
                ((RelayCommand)SendCommand).RaiseCanExecuteChanged();
                ((RelayCommand)DisconnectCommand).RaiseCanExecuteChanged();
                ((RelayCommand)SendFileCommand).RaiseCanExecuteChanged();
            }
        }

        public string Nickname
        {
            get => _nickname;
            set { _nickname = value; OnPropertyChanged(); ((RelayCommand)ConnectCommand).RaiseCanExecuteChanged(); } // Оновлюємо CanExecute при зміні ніка
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
            set
            {
                if (_isFileTransferring == value) return;
                _isFileTransferring = value;
                OnPropertyChanged();
                ((RelayCommand)SendFileCommand).RaiseCanExecuteChanged(); // Оновлюємо CanExecute
            }
        }


        public ICommand ConnectCommand { get; }
        public ICommand SendCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand SendFileCommand { get; }

        public MainWindowViewModel()
        {
            _tcpClientService = new TcpClientService();
            _tcpClientService.MessageReceived += OnMessageReceived;
            _tcpClientService.ConnectionStatusChanged += OnConnectionStatusChanged;
            _tcpClientService.UserListReceived += OnUserListReceived; // Цей рядок важливий

            ConnectCommand = new RelayCommand(async (o) => await ConnectToServerAsync(), (o) => !IsConnected && !string.IsNullOrWhiteSpace(Nickname));
            SendCommand = new RelayCommand(async (o) => await SendMessageAsync(), (o) => IsConnected && !string.IsNullOrWhiteSpace(MessageToSend));
            DisconnectCommand = new RelayCommand(async (o) => await DisconnectFromServerAsync(), (o) => IsConnected);
            SendFileCommand = new RelayCommand(async (o) => await SendFileAsync(), (o) => IsConnected && !IsFileTransferring);
        }

        private async Task ConnectToServerAsync()
        {
            if (string.IsNullOrWhiteSpace(Nickname))
            {
                AddSystemMessageToChat("Будь ласка, введіть нікнейм перед підключенням.");
                return;
            }
            // IsConnected перевіряється в CanExecute
            ConnectionStatus = $"Підключення до {_serverIp}:{_serverPort} як {Nickname}...";
            ChatMessages.Clear(); // Очищуємо чат перед новим підключенням
            OnlineUsers.Clear(); // Очищуємо список користувачів

            await _tcpClientService.ConnectAsync(_serverIp, _serverPort, Nickname);
            // Статус з'єднання оновиться через подію OnConnectionStatusChanged
        }

        private async Task SendMessageAsync()
        {
            var messageToActuallySend = MessageToSend.Trim();
            MessageToSend = string.Empty;

            if (string.IsNullOrWhiteSpace(messageToActuallySend)) return;

            await _tcpClientService.SendMessageAsync(messageToActuallySend);
            // Локальне відображення (сервер не надсилатиме це повідомлення назад відправнику)
            ChatMessages.Add(new Message { Text = $"Ви: {messageToActuallySend}", Timestamp = DateTime.Now, Sender = Nickname, IsOwnMessage = true });
        }

        private async Task SendFileAsync()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "All files (*.*)|*.*|Images (*.jpg;*.jpeg;*.png;*.gif)|*.jpg;*.jpeg;*.png;*.gif|Text files (*.txt)|*.txt",
                Title = "Оберіть файл для надсилання"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string filePath = openFileDialog.FileName;
                // ... (решта логіки SendFileAsync як у вас) ...
                string fileName = Path.GetFileName(filePath);
                string fileMimeType = MimeUtility.GetMimeMapping(fileName);
                long fileSize = new FileInfo(filePath).Length;
                const long MaxFileSize = 50 * 1024 * 1024;

                if (fileSize > MaxFileSize)
                {
                    AddSystemMessageToChat($"Файл занадто великий. Макс. розмір: {FormatFileSize(MaxFileSize)}.");
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

                var metadataMessage = new ChatMessage
                {
                    Type = MessageType.FileTransferMetadata,
                    Sender = Nickname, // Відправник встановлюється тут
                    FileId = fileId,
                    FileName = fileName,
                    FileSize = fileSize,
                    FileMimeType = fileMimeType,
                    TotalChunks = (int)Math.Ceiling((double)fileSize / (1024 * 64)), // Відразу розрахуємо
                    Content = $"Надсилання файлу: {fileName} ({FormatFileSize(fileSize)})"
                };
                await _tcpClientService.SendMessageObjectAsync(metadataMessage);
                AddSystemMessageToChat($"Розпочато надсилання файлу: {fileName}");

                int chunkSize = 1024 * 64;
                long totalChunksActual = (long)Math.Ceiling((double)fileSize / chunkSize);

                for (int i = 0; i < totalChunksActual; i++)
                {
                    if (!IsConnected)
                    {
                        AddSystemMessageToChat("З'єднання втрачено під час надсилання файлу.");
                        IsFileTransferring = false;
                        return;
                    }
                    int offset = i * chunkSize;
                    int length = Math.Min(chunkSize, (int)(fileSize - offset));
                    byte[] chunkBytes = new byte[length];
                    Buffer.BlockCopy(fileBytes, offset, chunkBytes, 0, length);

                    var chunkMessage = new ChatMessage
                    {
                        Type = MessageType.FileTransferChunk,
                        Sender = Nickname, // Відправник
                        FileId = fileId,
                        ChunkIndex = i,
                        TotalChunks = (int)totalChunksActual,
                        FileData = Convert.ToBase64String(chunkBytes)
                    };
                    await _tcpClientService.SendMessageObjectAsync(chunkMessage);
                    FileTransferProgress = (double)(i + 1) / totalChunksActual * 100;
                }

                var endMessage = new ChatMessage
                {
                    Type = MessageType.FileTransferEnd,
                    Sender = Nickname, // Відправник
                    FileId = fileId,
                    FileName = fileName,
                    Content = $"Файл '{fileName}' надіслано."
                };
                await _tcpClientService.SendMessageObjectAsync(endMessage);

                IsFileTransferring = false;
                FileTransferProgress = 100;
                ChatMessages.Add(new Message { Text = $"Ви надіслали файл: {fileName}", Timestamp = DateTime.Now, Sender = Nickname, IsOwnMessage = true });
            }
        }


        private async Task DisconnectFromServerAsync()
        {
            await _tcpClientService.DisconnectAsync();
            // Статус оновиться через OnConnectionStatusChanged
        }

        private void OnMessageReceived(ChatMessage receivedObject)
        {
            Task.Run(async () => await OnMessageReceivedAsync(receivedObject));
        }

        private async Task OnMessageReceivedAsync(ChatMessage receivedObject)
        {
            // Логування отриманого об'єкта
            Debug.WriteLine($"[VM.OnMessageReceivedAsync] Тип: {receivedObject.Type}, Відправник: {receivedObject.Sender}, Вміст: '{receivedObject.Content?.Substring(0, Math.Min(50, receivedObject.Content?.Length ?? 0))}'");

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
                            IsOwnMessage = receivedObject.Sender == Nickname
                        });
                        break;
                    case MessageType.SystemMessage:
                        if (receivedObject.Content == "Нікнейм вже зайнятий, спробуйте інший!")
                        {
                            ConnectionStatus = receivedObject.Content; // Показуємо повідомлення користувачу
                            // Не розриваємо з'єднання автоматично, даємо користувачу можливість змінити нік та спробувати знову
                            // Якщо сервер розірвав з'єднання, OnConnectionStatusChanged(false) спрацює.
                            AddSystemMessageToChat(receivedObject.Content); // Додаємо в чат для інформації
                            IsConnected = false; // Встановлюємо статус не підключено, щоб можна було спробувати знову
                        }
                        else
                        {
                            ChatMessages.Add(new Message { Text = receivedObject.Content, Timestamp = receivedObject.Timestamp, Sender = "System", IsSystemMessage = true });
                        }
                        break;
                    // Обробка UserList тепер відбувається в OnUserListReceived
                    // case MessageType.UserList: 
                    //    // ...
                    //    break; 
                    case MessageType.FileTransferMetadata:
                        // ... (ваша логіка FileTransferMetadata) ...
                        if (receivedObject.FileId != Guid.Empty && !string.IsNullOrEmpty(receivedObject.FileName))
                        {
                            AddSystemMessageToChat($"[{receivedObject.Timestamp:HH:mm:ss}] {receivedObject.Sender} хоче надіслати файл: {receivedObject.FileName} ({FormatFileSize(receivedObject.FileSize)}).");
                            _receivedFiles[receivedObject.FileId] = new FileReceptionState(receivedObject.FileName, receivedObject.FileSize, receivedObject.FileMimeType, (int)receivedObject.TotalChunks);
                        }
                        break;
                    case MessageType.FileTransferChunk:
                        // ... (ваша логіка FileTransferChunk) ...
                        if (receivedObject.FileId != Guid.Empty &&
                            _receivedFiles.TryGetValue(receivedObject.FileId, out FileReceptionState fileState) &&
                            !string.IsNullOrEmpty(receivedObject.FileData))
                        {
                            // ... (решта коду як у вас)
                        }
                        break;
                    case MessageType.FileTransferEnd:
                        // ... (ваша логіка FileTransferEnd) ...
                        if (receivedObject.FileId != Guid.Empty && _receivedFiles.ContainsKey(receivedObject.FileId))
                        {
                            // ...
                        }
                        break;

                    default:
                        if (receivedObject.Type != MessageType.UserList) // Переконуємось, що не логуємо UserList як невідомий
                        {
                            Debug.WriteLine($"[VM.OnMessageReceivedAsync] Отримано необроблений або невідомий тип повідомлення: {receivedObject.Type} від {receivedObject.Sender}");
                            // ChatMessages.Add(new Message { Text = $"Невідомий тип ({receivedObject.Type}) від {receivedObject.Sender}: {receivedObject.Content}", Timestamp = receivedObject.Timestamp, Sender = receivedObject.Sender });
                        }
                        break;
                }
            });
        }

        private void OnConnectionStatusChanged(bool isConnected)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                IsConnected = isConnected; // Це викличе оновлення PropertyChanged та CanExecute команд
                ConnectionStatus = isConnected ? $"Підключено як {Nickname}" : "Не підключено";
                if (!isConnected)
                {
                    OnlineUsers.Clear(); // Очищуємо список користувачів при відключенні
                    // ChatMessages.Clear(); // Можливо, не варто очищати повідомлення
                    AddSystemMessageToChat("З'єднання з сервером розірвано або не встановлено.");
                }
                else
                {
                    AddSystemMessageToChat("Успішно підключено до сервера!");
                }
            });
        }

        private void OnUserListReceived(List<string> users)
        {
            // Логування отриманого списку користувачів
            Debug.WriteLine($"[VM.OnUserListReceived] Отримано список користувачів: {string.Join(", ", users)}. Кількість: {users.Count}");

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                OnlineUsers.Clear();
                if (users != null) // Додаткова перевірка на null
                {
                    foreach (var user in users)
                    {
                        if (!string.IsNullOrWhiteSpace(user)) // Додаємо лише не порожні нікнейми
                        {
                            OnlineUsers.Add(user);
                        }
                    }
                }
                Debug.WriteLine($"[VM.OnUserListReceived] Колекція OnlineUsers оновлена. Поточна кількість: {OnlineUsers.Count}");
            });
        }

        private void AddSystemMessageToChat(string text)
        {
            System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ChatMessages.Add(new Message { Text = text, Timestamp = DateTime.Now, Sender = "System", IsSystemMessage = true });
            });
        }

        private string FormatFileSize(long bytes)
        {
            // ... (ваш код FormatFileSize) ...
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

        private class FileReceptionState
        {
            // ... (ваш клас FileReceptionState) ...
            public string FileName { get; }
            public long FileSize { get; }
            public string MimeType { get; }
            public int TotalChunks { get; }
            private readonly List<byte[]> _chunks;
            private int _receivedChunksCount;
            private readonly object _lock = new object();


            public FileReceptionState(string fileName, long fileSize, string mimeType, int totalChunks)
            {
                FileName = fileName;
                FileSize = fileSize;
                MimeType = mimeType;
                TotalChunks = totalChunks > 0 ? totalChunks : 1; // Захист від ділення на нуль, якщо TotalChunks = 0
                _chunks = new List<byte[]>(new byte[TotalChunks][]); // Ініціалізуємо з null елементами
                _receivedChunksCount = 0;
            }

            public void AddChunk(int index, byte[] data)
            {
                lock (_lock) // Блокування для потокобезпечного доступу
                {
                    if (index >= 0 && index < TotalChunks) // Перевірка індексу
                    {
                        if (_chunks[index] == null) // Додаємо чанк, тільки якщо він ще не був доданий
                        {
                            _chunks[index] = data;
                            _receivedChunksCount++;
                        }
                        else
                        {
                            Debug.WriteLine($"[FileReceptionState] Спроба додати вже існуючий чанк. Індекс: {index} для файлу {FileName}");
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"[FileReceptionState] Некоректний індекс чанку: {index}. TotalChunks: {TotalChunks} для файлу {FileName}");
                    }
                }
            }

            public bool IsComplete()
            {
                lock (_lock)
                {
                    // Переконуємося, що всі чанки отримані і жоден з них не null
                    return _receivedChunksCount == TotalChunks && _chunks.All(c => c != null);
                }
            }
            public byte[] GetAssembledFile()
            {
                lock (_lock)
                {
                    if (!IsComplete())
                    {
                        // Збираємо те, що є, для діагностики, якщо потрібно
                        Debug.WriteLine($"[FileReceptionState] Спроба зібрати неповний файл {FileName}. Отримано {_receivedChunksCount}/{TotalChunks} чанків.");
                        // throw new InvalidOperationException("Файл не повністю отримано.");
                        // Тимчасово дозволимо збирати неповний файл для діагностики
                        List<byte> assembledBytes = new List<byte>();
                        for (int i = 0; i < TotalChunks; i++)
                        {
                            if (_chunks[i] != null)
                            {
                                assembledBytes.AddRange(_chunks[i]);
                            }
                            else
                            {
                                // Якщо чанк відсутній, можна заповнити нулями або пропустити
                                Debug.WriteLine($"[FileReceptionState] Увага: Відсутній чанк {i} при збірці файлу {FileName}.");
                            }
                        }
                        return assembledBytes.ToArray();
                    }

                    // Коректна збірка
                    using (var ms = new MemoryStream((int)FileSize)) // Вказуємо очікуваний розмір
                    {
                        for (int i = 0; i < TotalChunks; i++)
                        {
                            if (_chunks[i] != null) // Додаткова перевірка
                            {
                                ms.Write(_chunks[i], 0, _chunks[i].Length);
                            }
                            else
                            {
                                // Цього не повинно статися, якщо IsComplete() повернув true
                                Debug.WriteLine($"[FileReceptionState] КРИТИЧНО: IsComplete true, але чанк {i} є null для файлу {FileName}.");
                                // Можна кинути виключення або заповнити нулями
                                // throw new InvalidOperationException($"Критична помилка збірки файлу: чанк {i} відсутній.");
                            }
                        }
                        return ms.ToArray();
                    }
                }
            }
            public double GetReceptionProgress()
            {
                lock (_lock)
                {
                    return TotalChunks == 0 ? 100.0 : (double)_receivedChunksCount / TotalChunks * 100.0;
                }
            }
        }
    }
    // RelayCommand залишається без змін
    public class RelayCommand : ICommand
    {
        // ... (код RelayCommand як у вас) ...
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
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public void RaiseCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }
    }
}