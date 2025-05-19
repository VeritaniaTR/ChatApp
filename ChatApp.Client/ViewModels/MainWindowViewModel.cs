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
using MimeMapping;
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
        private Dictionary<Guid, List<FileChunk>> _receivedFiles = new Dictionary<Guid, List<FileChunk>>();

        public ObservableCollection<Message> ChatMessages { get; } = new ObservableCollection<Message>();
        public ObservableCollection<string> OnlineUsers { get; } = new ObservableCollection<string>();

        public string ServerIp
        {
            get => _serverIp;
            set
            {
                _serverIp = value;
                OnPropertyChanged();
            }
        }

        public int ServerPort
        {
            get => _serverPort;
            set
            {
                _serverPort = value;
                OnPropertyChanged();
            }
        }

        public string MessageToSend
        {
            get => _messageToSend;
            set
            {
                _messageToSend = value;
                OnPropertyChanged();
            }
        }

        public string ConnectionStatus
        {
            get => _connectionStatus;
            set
            {
                _connectionStatus = value;
                OnPropertyChanged();
            }
        }

        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                _isConnected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ConnectionStatusColor));
            }
        }

        public string Nickname
        {
            get => _nickname;
            set
            {
                _nickname = value;
                OnPropertyChanged();
            }
        }

        public Brush ConnectionStatusColor => IsConnected ? Brushes.Green : Brushes.Red;

        public double FileTransferProgress
        {
            get => _fileTransferProgress;
            set
            {
                _fileTransferProgress = value;
                OnPropertyChanged();
            }
        }

        public bool IsFileTransferring
        {
            get => _isFileTransferring;
            set
            {
                _isFileTransferring = value;
                OnPropertyChanged();
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
            _tcpClientService.UserListReceived += OnUserListReceived;

            ConnectCommand = new RelayCommand(async (o) => await ConnectToServerAsync());
            SendCommand = new RelayCommand(async (o) => await SendMessageAsync());
            DisconnectCommand = new RelayCommand(async (o) => await DisconnectFromServerAsync());
            SendFileCommand = new RelayCommand(async (o) => await SendFileAsync());
        }
        private async Task ConnectToServerAsync()
        {
            if (IsConnected)
            {
                ConnectionStatus = "Вже підключено.";
                return;
            }

            if (string.IsNullOrWhiteSpace(Nickname))
            {
                ConnectionStatus = "Введіть нікнейм!";
                return;
            }

            IsConnected = false;
            ConnectionStatus = $"Підключення до {_serverIp}:{_serverPort}...";
            ChatMessages.Clear();
            OnlineUsers.Clear();

            await _tcpClientService.ConnectAsync(_serverIp, _serverPort, Nickname);
        }

        private async Task SendMessageAsync()
        {
            if (IsConnected && !string.IsNullOrEmpty(_messageToSend))
            {
                await _tcpClientService.SendMessageAsync(_messageToSend);
                ChatMessages.Add(new Message { Text = $"Ви: {_messageToSend}", Timestamp = DateTime.Now });
                MessageToSend = string.Empty;
            }
        }

        private async Task SendFileAsync()
        {
            if (!IsConnected)
            {
                ConnectionStatus = "Не підключено до сервера.";
                return;
            }

            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "All files (*.*)|*.*|Images (*.jpg;*.jpeg;*.png;*.gif)|*.jpg;*.jpeg;*.png;*.gif|Text files (*.txt)|*.txt";
            openFileDialog.Title = "Оберіть файл для надсилання";

            if (openFileDialog.ShowDialog() == true)
            {
                string filePath = openFileDialog.FileName;
                string fileName = Path.GetFileName(filePath);
                string fileMimeType = MimeMapping.MimeUtility.GetMimeMapping(fileName);

                long fileSize = new FileInfo(filePath).Length;

                const long MaxFileSize = 50 * 1024 * 1024; // 50 MB
                if (fileSize > MaxFileSize)
                {
                    ConnectionStatus = $"Файл занадто великий. Макс. розмір: {MaxFileSize / (1024 * 1024)} MB.";
                    return;
                }

                byte[] fileBytes;
                try
                {
                    fileBytes = File.ReadAllBytes(filePath);
                }
                catch (Exception ex)
                {
                    ConnectionStatus = $"Помилка читання файлу: {ex.Message}";
                    return;
                }

                Guid fileId = Guid.NewGuid();

                IsFileTransferring = true;
                FileTransferProgress = 0;

                var metadataMessage = new ChatMessage
                {
                    Type = MessageType.FileTransferMetadata,
                    Sender = Nickname,
                    FileId = fileId,
                    FileName = fileName,
                    FileSize = fileSize,
                    FileMimeType = fileMimeType,
                    Content = $"Надсилання файлу: {fileName} ({fileSize / 1024} KB)"
                };
                await _tcpClientService.SendMessageObjectAsync(metadataMessage);

                int chunkSize = 1024 * 64; // 64 KB фрагмент
                long totalChunks = (long)Math.Ceiling((double)fileSize / chunkSize);

                for (int i = 0; i < totalChunks; i++)
                {
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
                        FileData = Convert.ToBase64String(chunkBytes)
                    };
                    await _tcpClientService.SendMessageObjectAsync(chunkMessage);

                    FileTransferProgress = (double)(i + 1) / totalChunks * 100;
                }

                var endMessage = new ChatMessage
                {
                    Type = MessageType.FileTransferEnd,
                    Sender = Nickname,
                    FileId = fileId,
                    FileName = fileName,
                    Content = $"Файл '{fileName}' надіслано."
                };
                await _tcpClientService.SendMessageObjectAsync(endMessage);

                IsFileTransferring = false;
                FileTransferProgress = 100;

                ChatMessages.Add(new Message { Text = $"Ви надіслали файл: {fileName}", Timestamp = DateTime.Now });
            }
        }

        private async Task DisconnectFromServerAsync()
        {
            if (IsConnected)
            {
                await _tcpClientService.DisconnectAsync();
            }
        }

        private void OnMessageReceived(string message)
        {
            Task.Run(async () => await OnMessageReceivedAsync(ChatMessage.FromJson(message)));
        }

        private async Task OnMessageReceivedAsync(ChatMessage receivedObject)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                switch (receivedObject.Type)
                {
                    case MessageType.ChatMessage:
                    case MessageType.SystemMessage:
                        ChatMessages.Add(new Message { Text = receivedObject.Content, Timestamp = receivedObject.Timestamp, Sender = receivedObject.Sender });
                        break;
                    case MessageType.UserList:
                        List<string> users = receivedObject.Content.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                        OnlineUsers.Clear();
                        foreach (var user in users)
                        {
                            OnlineUsers.Add(user);
                        }
                        break;
                    case MessageType.Disconnect:
                        break;
                    case MessageType.FileTransferMetadata:
                        ChatMessages.Add(new Message { Text = $"[{receivedObject.Timestamp.ToString("HH:mm:ss")}] {receivedObject.Sender} хоче надіслати файл: {receivedObject.FileName} ({receivedObject.FileSize / 1024} KB).", Timestamp = receivedObject.Timestamp, Sender = receivedObject.Sender });
                        _receivedFiles[receivedObject.FileId] = new List<FileChunk>();
                        break;
                    case MessageType.FileTransferChunk:
                        if (receivedObject.FileId != Guid.Empty && receivedObject.FileData != null)
                        {
                            if (!_receivedFiles.ContainsKey(receivedObject.FileId))
                            {
                                _receivedFiles[receivedObject.FileId] = new List<FileChunk>();
                            }
                            _receivedFiles[receivedObject.FileId].Add(new FileChunk { ChunkIndex = receivedObject.ChunkIndex, FileData = receivedObject.FileData });
                            if (_receivedFiles[receivedObject.FileId].Count == receivedObject.TotalChunks)
                            {
                                string allData = string.Join("", _receivedFiles[receivedObject.FileId].OrderBy(c => c.ChunkIndex).Select(c => c.FileData));
                                byte[] fileBytes = Convert.FromBase64String(allData);
                                string tempFilePath = Path.Combine(Path.GetTempPath(), receivedObject.FileName);
                                File.WriteAllBytes(tempFilePath, fileBytes);

                                if (receivedObject.FileMimeType.StartsWith("image/"))
                                {
                                    BitmapImage bitmap = new BitmapImage();
                                    using (var ms = new MemoryStream(fileBytes))
                                    {
                                        bitmap.BeginInit();
                                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                        bitmap.StreamSource = ms;
                                        bitmap.EndInit();
                                    }
                                    ChatMessages.Add(new Message { Timestamp = receivedObject.Timestamp, Sender = receivedObject.Sender, Image = bitmap, IsImage = true });
                                }
                                else
                                {
                                    ChatMessages.Add(new Message { Text = $"[{receivedObject.Timestamp.ToString("HH:mm:ss")}] {receivedObject.Sender} файл '{receivedObject.FileName}' отримано та збережено: {tempFilePath}", Timestamp = receivedObject.Timestamp, FilePath = tempFilePath, IsImage = false, Sender = receivedObject.Sender });
                                }
                                _receivedFiles.Remove(receivedObject.FileId);
                            }
                        }
                        break;
                    case MessageType.FileTransferEnd:
                        break;
                    default:
                        ChatMessages.Add(new Message { Text = receivedObject.Content, Timestamp = receivedObject.Timestamp, Sender = receivedObject.Sender });
                        break;
                }
            });
        }

        private void OnConnectionStatusChanged(bool isConnected)
        {
            IsConnected = isConnected;
            ConnectionStatus = isConnected ? "Підключено" : "Не підключено";
            if (!isConnected)
            {
                OnlineUsers.Clear();
                ChatMessages.Clear();
            }
        }

        private void OnUserListReceived(List<string> users)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                OnlineUsers.Clear();
                foreach (var user in users)
                {
                    OnlineUsers.Add(user);
                }
            });
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    internal class FileChunk
    {
        public int ChunkIndex { get; set; }
        public string FileData { get; set; }
    }

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
    }
}