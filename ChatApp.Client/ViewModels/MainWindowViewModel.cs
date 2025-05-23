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
                // НЕ викликаємо RaiseCanExecuteChanged для SendCommand тут,
                // щоб уникнути потенційної рекурсії при кожному введенні символу.
                // Стан команди оновиться при зміні IsConnected або після SendMessageAsync.
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
                RefreshAllCommandsCanExecuteState(); // Оновлюємо всі команди при зміні статусу підключення
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
                ((RelayCommand)ConnectCommand).RaiseCanExecuteChanged(); // Тільки ConnectCommand залежить від Nickname
                UpdateConnectionStatusText();
            }
        }

        public Brush ConnectionStatusColor => IsConnected ? Brushes.Green : Brushes.Red;
        public double FileTransferProgress { get => _fileTransferProgress; set { /* ... (код як раніше) ... */ } }
        public bool IsFileTransferring { get => _isFileTransferring; set { /* ... (код як раніше, з RaiseCanExecuteChanged для SendFileCommand) ... */ } }


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
                (o) => IsConnected && !string.IsNullOrWhiteSpace(MessageToSend)); // CanExecute все ще залежить від MessageToSend
            DisconnectCommand = new RelayCommand(async (o) => await DisconnectFromServerAsync(),
                (o) => IsConnected);
            SendFileCommand = new RelayCommand(async (o) => await SendFileAsync(),
                (o) => IsConnected && !IsFileTransferring);

            UpdateConnectionStatusText();
        }

        private void RefreshAllCommandsCanExecuteState()
        {
            Debug.WriteLine("[VM.RefreshAllCommandsCanExecuteState] Оновлення стану всіх команд...");
            System.Windows.Application.Current?.Dispatcher?.Invoke(() => {
                ((RelayCommand)ConnectCommand).RaiseCanExecuteChanged();
                ((RelayCommand)SendCommand).RaiseCanExecuteChanged();
                ((RelayCommand)DisconnectCommand).RaiseCanExecuteChanged();
                ((RelayCommand)SendFileCommand).RaiseCanExecuteChanged();
            });
        }

        private void UpdateConnectionStatusText()
        { ConnectionStatus = IsConnected ? $"Підключено як {Nickname}" : "Не підключено"; }

        private void SetConnectionState(bool newIsConnectedStatus)
        {
            if (_isConnected == newIsConnectedStatus) { UpdateConnectionStatusText(); return; }
            IsConnected = newIsConnectedStatus;
            if (!newIsConnectedStatus) { OnlineUsers.Clear(); AddSystemMessageToChat("З'єднання з сервером розірвано."); }
            else { AddSystemMessageToChat("Успішно підключено!"); }
        }

        private async Task ConnectToServerAsync()
        {
            if (!((RelayCommand)ConnectCommand).CanExecute(null)) { if (string.IsNullOrWhiteSpace(Nickname)) AddSystemMessageToChat("Будь ласка, введіть нікнейм."); return; }
            string connectingStatus = $"Підключення до {_serverIp}:{_serverPort} як {Nickname}...";
            System.Windows.Application.Current.Dispatcher.Invoke(() => { if (ConnectionStatus != connectingStatus) ConnectionStatus = connectingStatus; });
            ChatMessages.Clear(); OnlineUsers.Clear();
            await _tcpClientService.ConnectAsync(_serverIp, _serverPort, Nickname);
        }

        private async Task SendMessageAsync()
        {
            // Перевіряємо CanExecute на початку
            if (!((RelayCommand)SendCommand).CanExecute(null))
            {
                Debug.WriteLine("[VM.SendMessageAsync] SendCommand.CanExecute = false. Вихід.");
                return;
            }

            var messageToActuallySend = MessageToSend?.Trim(); // ?. для безпеки, хоча CanExecute вже перевірив

            // Очищуємо поле MessageToSend. Це викличе OnPropertyChanged для MessageToSend,
            // але НЕ викличе RaiseCanExecuteChanged для SendCommand з сеттера.
            MessageToSend = string.Empty;

            if (string.IsNullOrWhiteSpace(messageToActuallySend))
            {
                Debug.WriteLine("[VM.SendMessageAsync] Повідомлення порожнє після Trim, вихід.");
                // Явно оновлюємо стан кнопки SendCommand тут, оскільки MessageToSend змінився.
                ((RelayCommand)SendCommand).RaiseCanExecuteChanged();
                return;
            }

            try
            {
                await _tcpClientService.SendMessageAsync(messageToActuallySend);
                ChatMessages.Add(new Message { Text = $"Ви: {messageToActuallySend}", Timestamp = DateTime.Now, Sender = Nickname, IsOwnMessage = true });
            }
            catch (Exception ex)
            {
                AddSystemMessageToChat($"Помилка надсилання: {ex.Message}");
            }
            finally
            {
                // Оновлюємо стан кнопки SendCommand після всіх операцій
                ((RelayCommand)SendCommand).RaiseCanExecuteChanged();
            }
        }

        public async Task SendFileAsync() { /* ... код SendFileAsync як у попередній повній версії ... */ await Task.CompletedTask; }
        private async Task DisconnectFromServerAsync() { await _tcpClientService.DisconnectAsync(); }
        private void OnMessageReceived(ChatMessage receivedObject) { Task.Run(async () => await OnMessageReceivedAsync(receivedObject)); }
        private async Task OnMessageReceivedAsync(ChatMessage receivedObject) { /* ... код обробки як у попередній повній версії ... */ await Task.CompletedTask; }
        private void OnConnectionStatusChanged(bool newIsConnectedStatus) { System.Windows.Application.Current.Dispatcher.Invoke(() => { SetConnectionState(newIsConnectedStatus); }); }
        private void OnUserListReceived(List<string> users) { System.Windows.Application.Current.Dispatcher.Invoke(() => { OnlineUsers.Clear(); if (users != null) { foreach (var user in users) { if (!string.IsNullOrWhiteSpace(user)) OnlineUsers.Add(user); } } }); }
        private void AddSystemMessageToChat(string text) { System.Windows.Application.Current.Dispatcher.InvokeAsync(() => { ChatMessages.Add(new Message { Text = text, Timestamp = DateTime.Now, Sender = "System", IsSystemMessage = true }); }); }
        private string FormatFileSize(long bytes) { /* ... */ return string.Empty; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        private class FileReceptionState
        { /* ... як у попередній повній версії, з виправленням ініціалізації _chunks ... */
            public string FileName { get; }
            public long FileSize { get; }
            public string MimeType { get; }
            public int TotalChunks { get; }
            private readonly List<byte[]> _chunks; private int _receivedChunksCount; private readonly object _lock = new object();
            public FileReceptionState(string fileName, long fileSize, string mimeType, int totalChunks) { FileName = fileName; FileSize = fileSize; MimeType = mimeType; TotalChunks = totalChunks > 0 ? totalChunks : 1; _chunks = new List<byte[]>(new byte[TotalChunks][]); _receivedChunksCount = 0; }
            public void AddChunk(int index, byte[] data) { lock (_lock) { if (index >= 0 && index < TotalChunks) { if (_chunks[index] == null) { _chunks[index] = data; _receivedChunksCount++; } } } }
            public bool IsComplete() { lock (_lock) { return _receivedChunksCount == TotalChunks && _chunks.All(c => c != null); } }
            public byte[] GetAssembledFile() { lock (_lock) { List<byte> assembledBytes = new List<byte>(); if (!IsComplete()) { for (int i = 0; i < TotalChunks; i++) { if (i < _chunks.Count && _chunks[i] != null) assembledBytes.AddRange(_chunks[i]); } return assembledBytes.ToArray(); } using (var ms = new MemoryStream(FileSize > 0 ? (int)FileSize : 1024)) { for (int i = 0; i < TotalChunks; i++) { if (i < _chunks.Count && _chunks[i] != null) ms.Write(_chunks[i], 0, _chunks[i].Length); } return ms.ToArray(); } } }
            public double GetReceptionProgress() { lock (_lock) { return TotalChunks == 0 ? 100.0 : ((double)_receivedChunksCount / TotalChunks * 100.0); } }
        }
    }

    public class RelayCommand : ICommand
    { /* ... як у попередній повній версії ... */
        private readonly Func<object, Task> _executeAsync; private readonly Action<object> _executeSync; private readonly Predicate<object> _canExecute; private readonly bool _isAsync;
        public RelayCommand(Action<object> execute, Predicate<object> canExecute = null) { _executeSync = execute ?? throw new ArgumentNullException(nameof(execute)); _canExecute = canExecute; _isAsync = false; }
        public RelayCommand(Func<object, Task> executeAsync, Predicate<object> canExecute = null) { _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync)); _canExecute = canExecute; _isAsync = true; }
        public bool CanExecute(object parameter) => _canExecute == null || _canExecute(parameter);
        public async void Execute(object parameter) { if (!CanExecute(parameter)) return; if (_isAsync) await _executeAsync(parameter); else _executeSync(parameter); }
        public event EventHandler CanExecuteChanged { add { CommandManager.RequerySuggested += value; } remove { CommandManager.RequerySuggested -= value; } }
        public void RaiseCanExecuteChanged() { CommandManager.InvalidateRequerySuggested(); }
    }
}