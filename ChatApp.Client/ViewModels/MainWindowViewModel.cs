using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
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
        private string _connectionStatus;
        private bool _isConnected = false;
        private readonly TcpClientService _tcpClientService;
        private string _nickname;

        private double _fileTransferProgress;
        private bool _isFileTransferring;
        private readonly Dictionary<Guid, FileReceptionState> _receivedFiles = new Dictionary<Guid, FileReceptionState>();

        public ObservableCollection<Message> ChatMessages { get; }
        public ObservableCollection<string> OnlineUsers { get; }

        public string ServerIp
        {
            get => _serverIp;
            set { if (_serverIp == value) return; _serverIp = value; OnPropertyChanged(); }
        }

        public int ServerPort
        {
            get => _serverPort;
            set { if (_serverPort == value) return; _serverPort = value; OnPropertyChanged(); }
        }

        public string MessageToSend
        {
            get => _messageToSend;
            set
            {
                if (_messageToSend == value) return;
                _messageToSend = value;
                OnPropertyChanged();
                ((RelayCommand)SendCommand).RaiseCanExecuteChanged();
            }
        }

        public string ConnectionStatus
        {
            get => _connectionStatus;
            private set { if (_connectionStatus == value) return; _connectionStatus = value; OnPropertyChanged(); }
        }

        public bool IsConnected
        {
            get => _isConnected;
            private set
            {
                if (_isConnected == value) return;
                _isConnected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ConnectionStatusColor));
                UpdateConnectionStatusText();
                RefreshAllCommandsCanExecuteState();
            }
        }

        public string Nickname
        {
            get => _nickname;
            set
            {
                if (_nickname == value) return;
                _nickname = value;
                OnPropertyChanged();
                ((RelayCommand)ConnectCommand).RaiseCanExecuteChanged();
                UpdateConnectionStatusText();
            }
        }

        public Brush ConnectionStatusColor => IsConnected ? Brushes.Green : Brushes.Red;

        public double FileTransferProgress
        {
            get => _fileTransferProgress;
            set
            {
                if (_fileTransferProgress == value) return;
                _fileTransferProgress = value;
                OnPropertyChanged();
            }
        }

        public bool IsFileTransferring
        {
            get => _isFileTransferring;
            set
            {
                if (_isFileTransferring == value) return;
                _isFileTransferring = value;
                OnPropertyChanged();
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    ((RelayCommand)SendFileCommand).RaiseCanExecuteChanged();
                });
            }
        }


        public ICommand ConnectCommand { get; }
        public ICommand SendCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand SendFileCommand { get; }

        public MainWindowViewModel()
        {
            ChatMessages = new ObservableCollection<Message>();
            OnlineUsers = new ObservableCollection<string>();

            _tcpClientService = new TcpClientService();
            _tcpClientService.MessageReceived += OnMessageReceived;
            _tcpClientService.ConnectionStatusChanged += OnConnectionStatusChanged;
            _tcpClientService.UserListReceived += OnUserListReceived;

            ConnectCommand = new RelayCommand(async (o) => await ConnectToServerAsync(),
                (o) => !IsConnected && !string.IsNullOrWhiteSpace(Nickname));
            SendCommand = new RelayCommand(async (o) => await SendMessageAsync(),
                (o) => IsConnected && !string.IsNullOrWhiteSpace(MessageToSend));
            DisconnectCommand = new RelayCommand(async (o) => await DisconnectFromServerAsync(),
                (o) => IsConnected);
            SendFileCommand = new RelayCommand(async (o) => await SendFileAsync(),
                (o) => IsConnected && !IsFileTransferring);

            UpdateConnectionStatusText();
        }

        private void RefreshAllCommandsCanExecuteState()
        {
            Debug.WriteLine("[VM.RefreshAllCommandsCanExecuteState] Refreshing state of all commands...");
            System.Windows.Application.Current?.Dispatcher?.Invoke(() => {
                ((RelayCommand)ConnectCommand).RaiseCanExecuteChanged();
                ((RelayCommand)SendCommand).RaiseCanExecuteChanged();
                ((RelayCommand)DisconnectCommand).RaiseCanExecuteChanged();
                ((RelayCommand)SendFileCommand).RaiseCanExecuteChanged();
            });
        }

        private void UpdateConnectionStatusText()
        { ConnectionStatus = IsConnected ? $"Connected as {Nickname}" : "Not connected"; }

        private void SetConnectionState(bool newIsConnectedStatus)
        {
            bool previousIsConnected = IsConnected;
            IsConnected = newIsConnectedStatus;

            if (previousIsConnected && !newIsConnectedStatus)
            {
                OnlineUsers.Clear();
                AddSystemMessageToChat("Connection to server lost.");
            }
            else if (!previousIsConnected && newIsConnectedStatus)
            {
                AddSystemMessageToChat("Successfully connected!");
            }
        }

        private async Task ConnectToServerAsync()
        {
            if (!((RelayCommand)ConnectCommand).CanExecute(null)) { if (string.IsNullOrWhiteSpace(Nickname)) AddSystemMessageToChat("Please enter a nickname."); return; }
            string connectingStatus = $"Connecting to {_serverIp}:{_serverPort} as {Nickname}...";
            System.Windows.Application.Current.Dispatcher.Invoke(() => { if (ConnectionStatus != connectingStatus) ConnectionStatus = connectingStatus; });
            ChatMessages.Clear(); OnlineUsers.Clear();
            AddSystemMessageToChat(connectingStatus);
            await _tcpClientService.ConnectAsync(_serverIp, _serverPort, Nickname);
        }

        private async Task SendMessageAsync()
        {
            if (!((RelayCommand)SendCommand).CanExecute(null))
            {
                Debug.WriteLine("[VM.SendMessageAsync] SendCommand.CanExecute = false. Exiting.");
                return;
            }

            var messageToActuallySend = MessageToSend?.Trim();
            MessageToSend = string.Empty;

            if (string.IsNullOrWhiteSpace(messageToActuallySend))
            {
                Debug.WriteLine("[VM.SendMessageAsync] Message is empty after Trim, exiting.");
                return;
            }

            try
            {
                ChatMessages.Add(new Message { Text = $"You: {messageToActuallySend}", Timestamp = DateTime.Now, Sender = Nickname, IsOwnMessage = true });
                await _tcpClientService.SendMessageAsync(messageToActuallySend);
            }
            catch (Exception ex)
            {
                AddSystemMessageToChat($"Error sending message: {ex.Message}");
            }
        }

        private bool IsMimeTypeImage(string mimeType)
        {
            if (string.IsNullOrWhiteSpace(mimeType))
                return false;
            return mimeType.ToLower().StartsWith("image/");
        }

        private BitmapImage LoadBitmapImage(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                Debug.WriteLine($"[VM.LoadBitmapImage] File not found or path is empty: {filePath}");
                return null;
            }

            try
            {
                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VM.LoadBitmapImage] Error loading image from {filePath}: {ex.Message}");
                return null;
            }
        }

        public async Task SendFileAsync()
        {
            if (!IsConnected || IsFileTransferring) return;

            OpenFileDialog openFileDialog = new OpenFileDialog();
            string originalFilePath = string.Empty;

            if (openFileDialog.ShowDialog() == true)
            {
                originalFilePath = openFileDialog.FileName;
                string fileName = Path.GetFileName(originalFilePath);
                long fileSize;
                try
                {
                    fileSize = new FileInfo(originalFilePath).Length;
                }
                catch (Exception ex)
                {
                    AddSystemMessageToChat($"Error getting file info for '{fileName}': {ex.Message}");
                    return;
                }
                string mimeType = MimeUtility.GetMimeMapping(fileName);

                IsFileTransferring = true;
                FileTransferProgress = 0;
                AddSystemMessageToChat($"Sending file: {fileName} ({FormatFileSize(fileSize)})...");

                try
                {
                    Guid fileId = Guid.NewGuid();
                    const int chunkSize = 64 * 1024;
                    int totalChunks = (int)Math.Ceiling((double)fileSize / chunkSize);
                    if (totalChunks == 0 && fileSize > 0) totalChunks = 1;
                    if (fileSize == 0) totalChunks = 1;

                    var metadataMessage = new ChatMessage
                    {
                        Type = MessageType.FileTransferMetadata,
                        Sender = Nickname,
                        FileId = fileId,
                        FileName = fileName,
                        FileSize = fileSize,
                        FileMimeType = mimeType,
                        TotalChunks = totalChunks,
                        Timestamp = DateTime.UtcNow
                    };
                    await _tcpClientService.SendMessageObjectAsync(metadataMessage);
                    Debug.WriteLine($"[VM.SendFileAsync] Sent metadata for file {fileName}, ID: {fileId}, Chunks: {totalChunks}");

                    using (FileStream fs = new FileStream(originalFilePath, FileMode.Open, FileAccess.Read))
                    {
                        byte[] buffer = new byte[chunkSize];
                        int bytesRead;
                        for (int i = 0; i < totalChunks; i++)
                        {
                            if (!IsConnected) { AddSystemMessageToChat("File transfer cancelled: connection lost."); break; }

                            bytesRead = await fs.ReadAsync(buffer, 0, chunkSize);
                            if (bytesRead == 0) break;

                            byte[] chunkData = new byte[bytesRead];
                            Array.Copy(buffer, chunkData, bytesRead);

                            var chunkMessage = new ChatMessage
                            {
                                Type = MessageType.FileTransferChunk,
                                Sender = Nickname,
                                FileId = fileId,
                                FileName = fileName,
                                ChunkIndex = i,
                                TotalChunks = totalChunks,
                                FileData = Convert.ToBase64String(chunkData),
                                Timestamp = DateTime.UtcNow
                            };
                            await _tcpClientService.SendMessageObjectAsync(chunkMessage);
                            FileTransferProgress = ((double)(i + 1) / totalChunks) * 100;
                        }
                    }
                    if (!IsConnected) throw new Exception("Connection lost during file transfer.");

                    var endMessage = new ChatMessage
                    {
                        Type = MessageType.FileTransferEnd,
                        Sender = Nickname,
                        FileId = fileId,
                        FileName = fileName,
                        FileSize = fileSize,
                        FileMimeType = mimeType,
                        Timestamp = DateTime.UtcNow
                    };
                    await _tcpClientService.SendMessageObjectAsync(endMessage);
                    Debug.WriteLine($"[VM.SendFileAsync] Sent FileTransferEnd for file {fileName}, ID: {fileId}");

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        Message sentMessage = new Message
                        {
                            Text = $"You sent a file:",
                            FilePath = originalFilePath,
                            Timestamp = DateTime.Now,
                            Sender = Nickname,
                            IsOwnMessage = true
                        };

                        if (IsMimeTypeImage(mimeType))
                        {
                            sentMessage.IsImage = true;
                            sentMessage.Image = LoadBitmapImage(originalFilePath);
                        }

                        ChatMessages.Add(sentMessage);
                    });
                }
                catch (Exception ex)
                {
                    AddSystemMessageToChat($"Error sending file '{fileName}': {ex.Message}");
                    Debug.WriteLine($"[VM.SendFileAsync] Error: {ex}");
                }
                finally
                {
                    IsFileTransferring = false;
                    FileTransferProgress = 0;
                }
            }
        }

        private async Task DisconnectFromServerAsync()
        {
            await _tcpClientService.DisconnectAsync();
        }

        private void OnMessageReceived(ChatMessage receivedObject)
        {
            Task.Run(async () => await OnMessageReceivedAsync(receivedObject));
        }

        private async Task OnMessageReceivedAsync(ChatMessage receivedObject)
        {
            if (receivedObject == null)
            {
                Debug.WriteLine("[VM.OnMessageReceivedAsync] Received null ChatMessage. Ignoring.");
                return;
            }

            Debug.WriteLine($"[VM.OnMessageReceivedAsync] Received message: Type={receivedObject.Type}, Sender={receivedObject.Sender}, Content='{receivedObject.Content?.Substring(0, Math.Min(receivedObject.Content?.Length ?? 0, 50))}'");

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                switch (receivedObject.Type)
                {
                    case MessageType.ChatMessage:
                        if (receivedObject.Sender == Nickname)
                        {
                            Debug.WriteLine($"[VM.OnMessageReceivedAsync] Ignoring own ChatMessage from {receivedObject.Sender} (possibly echo).");
                        }
                        else
                        {
                            ChatMessages.Add(new Message
                            {
                                Text = $"{receivedObject.Sender}: {receivedObject.Content}",
                                Timestamp = receivedObject.Timestamp.ToLocalTime(),
                                Sender = receivedObject.Sender,
                                IsOwnMessage = false,
                                IsSystemMessage = false
                            });
                        }
                        break;

                    case MessageType.SystemMessage:
                        if (receivedObject.Content == "Nickname is already taken, please try another one!")
                        {
                            AddSystemMessageToChat($"Connection error: {receivedObject.Content}");
                            IsConnected = false;
                        }
                        else
                        {
                            ChatMessages.Add(new Message
                            {
                                Text = $"[System]: {receivedObject.Content}",
                                Timestamp = receivedObject.Timestamp.ToLocalTime(),
                                Sender = "System",
                                IsSystemMessage = true
                            });
                        }
                        break;

                    case MessageType.UserList:
                        Debug.WriteLine("[VM.OnMessageReceivedAsync] Received UserList, handled by OnUserListReceived.");
                        break;

                    case MessageType.HistoricFileMessage:
                        string historicFileName = receivedObject.FileName;
                        string downloadsPathHistoric = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                        string receivedFilesDirHistoric = Path.Combine(downloadsPathHistoric, "ChatAppReceivedFiles");
                        string historicFilePath = Path.Combine(receivedFilesDirHistoric, historicFileName);

                        Message historicMessage = new Message
                        {
                            Text = $"{receivedObject.Sender} sent a file (history):",
                            FilePath = File.Exists(historicFilePath) ? historicFilePath : historicFileName,
                            Timestamp = receivedObject.Timestamp.ToLocalTime(),
                            Sender = receivedObject.Sender,
                            IsOwnMessage = receivedObject.Sender == Nickname,
                            IsSystemMessage = false,
                        };

                        if (IsMimeTypeImage(receivedObject.FileMimeType) && File.Exists(historicFilePath))
                        {
                            historicMessage.IsImage = true;
                            historicMessage.Image = LoadBitmapImage(historicFilePath);
                        }

                        ChatMessages.Add(historicMessage);
                        break;

                    case MessageType.FileTransferMetadata:
                        if (receivedObject.Sender != Nickname)
                        {
                            AddSystemMessageToChat($"{receivedObject.Sender} is starting to send a file: {receivedObject.FileName} ({FormatFileSize(receivedObject.FileSize)}).");
                            _receivedFiles[receivedObject.FileId] = new FileReceptionState(
                                receivedObject.FileName,
                                receivedObject.FileSize,
                                receivedObject.FileMimeType,
                                receivedObject.TotalChunks
                            );
                        }
                        break;

                    case MessageType.FileTransferChunk:
                        if (receivedObject.Sender != Nickname && _receivedFiles.TryGetValue(receivedObject.FileId, out var fileStateChunk))
                        {
                            try
                            {
                                byte[] chunkData = Convert.FromBase64String(receivedObject.FileData);
                                fileStateChunk.AddChunk(receivedObject.ChunkIndex, chunkData);
                                Debug.WriteLine($"[VM.OnMessageReceivedAsync] Received chunk {receivedObject.ChunkIndex + 1}/{fileStateChunk.TotalChunks} for file {fileStateChunk.FileName}. Progress: {fileStateChunk.GetReceptionProgress():F1}%");

                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[VM.OnMessageReceivedAsync] Error processing chunk for file {receivedObject.FileId}: {ex.Message}");
                                _receivedFiles.Remove(receivedObject.FileId);
                            }
                        }
                        break;

                    case MessageType.FileTransferEnd:
                        if (receivedObject.Sender != Nickname && _receivedFiles.TryGetValue(receivedObject.FileId, out var fileStateEnd))
                        {
                            if (fileStateEnd.IsComplete())
                            {
                                byte[] assembledFile = fileStateEnd.GetAssembledFile();
                                string downloadsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                                string receivedFilesDir = Path.Combine(downloadsPath, "ChatAppReceivedFiles");
                                Directory.CreateDirectory(receivedFilesDir);
                                string savePath = Path.Combine(receivedFilesDir, fileStateEnd.FileName);

                                try
                                {
                                    File.WriteAllBytes(savePath, assembledFile);

                                    Message newMessage = new Message
                                    {
                                        Text = $"{receivedObject.Sender} sent a file:",
                                        FilePath = savePath,
                                        Timestamp = receivedObject.Timestamp.ToLocalTime(),
                                        Sender = receivedObject.Sender,
                                        IsOwnMessage = false
                                    };

                                    if (IsMimeTypeImage(fileStateEnd.MimeType))
                                    {
                                        newMessage.IsImage = true;
                                        newMessage.Image = LoadBitmapImage(savePath);
                                    }

                                    ChatMessages.Add(newMessage);
                                    Debug.WriteLine($"[VM.OnMessageReceivedAsync] File {fileStateEnd.FileName} successfully received and saved to {savePath}. Size: {assembledFile.Length} bytes.");
                                }
                                catch (Exception ex)
                                {
                                    AddSystemMessageToChat($"Error saving file {fileStateEnd.FileName}: {ex.Message}");
                                    Debug.WriteLine($"[VM.OnMessageReceivedAsync] Error saving file {fileStateEnd.FileName}: {ex.Message}");
                                }
                            }
                            else
                            {
                                AddSystemMessageToChat($"Error receiving file {fileStateEnd.FileName}: not all parts received.");
                                Debug.WriteLine($"[VM.OnMessageReceivedAsync] File {fileStateEnd.FileName} not complete. Received {fileStateEnd.GetReceptionProgress():F1}%");
                            }
                            _receivedFiles.Remove(receivedObject.FileId);
                        }
                        else if (receivedObject.Sender == Nickname)
                        {
                            Debug.WriteLine($"[VM.OnMessageReceivedAsync] Received FileTransferEnd confirmation for my file {receivedObject.FileName}.");
                        }
                        break;

                    default:
                        Debug.WriteLine($"[VM.OnMessageReceivedAsync] Unknown or unhandled message type: {receivedObject.Type}");
                        break;
                }
            });
        }


        private void OnConnectionStatusChanged(bool newIsConnectedStatus)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                SetConnectionState(newIsConnectedStatus);
            });
        }

        private void OnUserListReceived(List<string> users)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                OnlineUsers.Clear();
                if (users != null)
                {
                    foreach (var user in users)
                    {
                        if (!string.IsNullOrWhiteSpace(user) && user != "UnknownUser")
                            OnlineUsers.Add(user);
                    }
                }
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
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            double dblSByte = bytes;
            if (bytes > 1024)
            {
                for (i = 0; (bytes / 1024) > 0 && i < suffixes.Length - 1; i++, bytes /= 1024)
                    dblSByte = bytes / 1024.0;
            }
            return String.Format("{0:0.##} {1}", dblSByte, suffixes[i]);
        }


        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private class FileReceptionState
        {
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
                TotalChunks = totalChunks > 0 ? totalChunks : 1;
                _chunks = new List<byte[]>(new byte[TotalChunks][]);
                _receivedChunksCount = 0;
            }

            public void AddChunk(int index, byte[] data)
            {
                lock (_lock)
                {
                    if (index >= 0 && index < TotalChunks)
                    {
                        if (_chunks[index] == null)
                        {
                            _chunks[index] = data;
                            _receivedChunksCount++;
                        }
                        else
                        {
                            Debug.WriteLine($"[FileReceptionState] Duplicate chunk {index} for file {FileName}. Ignoring.");
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"[FileReceptionState] Invalid chunk index {index} (Total: {TotalChunks}) for file {FileName}.");
                    }
                }
            }

            public bool IsComplete()
            {
                lock (_lock)
                {
                    return _receivedChunksCount == TotalChunks && _chunks.All(c => c != null);
                }
            }

            public byte[] GetAssembledFile()
            {
                lock (_lock)
                {
                    if (!IsComplete())
                    {
                        Debug.WriteLine($"[FileReceptionState] Attempting to assemble incomplete file {FileName}. Received {_receivedChunksCount}/{TotalChunks} chunks.");
                        List<byte> assembledBytesOnError = new List<byte>();
                        for (int i = 0; i < TotalChunks; i++)
                        {
                            if (i < _chunks.Count && _chunks[i] != null)
                            {
                                assembledBytesOnError.AddRange(_chunks[i]);
                            }
                        }
                        return assembledBytesOnError.ToArray();
                    }

                    using (var ms = new MemoryStream(FileSize > 0 ? (int)FileSize : (TotalChunks > 0 ? TotalChunks * 1024 : 1024)))
                    {
                        for (int i = 0; i < TotalChunks; i++)
                        {
                            if (i < _chunks.Count && _chunks[i] != null)
                            {
                                ms.Write(_chunks[i], 0, _chunks[i].Length);
                            }
                            else
                            {
                                Debug.WriteLine($"[FileReceptionState] CRITICAL: Missing chunk {i} when assembling file {FileName}, though IsComplete()=true.");
                                throw new InvalidOperationException($"Missing chunk {i} when assembling file {FileName}.");
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
                    return TotalChunks == 0 ? 100.0 : ((double)_receivedChunksCount / TotalChunks * 100.0);
                }
            }
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Func<object, Task> _executeAsync; private readonly Action<object> _executeSync; private readonly Predicate<object> _canExecute; private readonly bool _isAsync;
        public RelayCommand(Action<object> execute, Predicate<object> canExecute = null) { _executeSync = execute ?? throw new ArgumentNullException(nameof(execute)); _canExecute = canExecute; _isAsync = false; }
        public RelayCommand(Func<object, Task> executeAsync, Predicate<object> canExecute = null) { _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync)); _canExecute = canExecute; _isAsync = true; }
        public bool CanExecute(object parameter) => _canExecute == null || _canExecute(parameter);
        public async void Execute(object parameter) { if (!CanExecute(parameter)) return; if (_isAsync) await _executeAsync(parameter); else _executeSync(parameter); }
        public event EventHandler CanExecuteChanged { add { CommandManager.RequerySuggested += value; } remove { CommandManager.RequerySuggested -= value; } }
        public void RaiseCanExecuteChanged() { CommandManager.InvalidateRequerySuggested(); }
    }
}