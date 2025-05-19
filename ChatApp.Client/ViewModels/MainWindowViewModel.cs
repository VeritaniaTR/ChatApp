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
                // ТИМЧАСОВО: Закоментовано для діагностики StackOverflow
                // ((RelayCommand)SendCommand).RaiseCanExecuteChanged();
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

                // ТИМЧАСОВО: Оновлюємо тільки необхідні команди
                ((RelayCommand)ConnectCommand).RaiseCanExecuteChanged();
                ((RelayCommand)DisconnectCommand).RaiseCanExecuteChanged();
                ((RelayCommand)SendCommand).RaiseCanExecuteChanged(); // Важливо для кнопки Send
                ((RelayCommand)SendFileCommand).RaiseCanExecuteChanged();
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
        public double FileTransferProgress { get => _fileTransferProgress; set { /* ... */ } } // Скорочено для прикладу
        public bool IsFileTransferring { get => _isFileTransferring; set { /* ... */ } } // Скорочено для прикладу

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

            ConnectCommand = new RelayCommand(async (o) => await ConnectToServerAsync(), (o) => !IsConnected && !string.IsNullOrWhiteSpace(Nickname));
            SendCommand = new RelayCommand(async (o) => await SendMessageAsync(), (o) => IsConnected && !string.IsNullOrWhiteSpace(MessageToSend)); // CanExecute залишається
            DisconnectCommand = new RelayCommand(async (o) => await DisconnectFromServerAsync(), (o) => IsConnected);
            SendFileCommand = new RelayCommand(async (o) => await SendFileAsync(), (o) => IsConnected && !IsFileTransferring);

            UpdateConnectionStatusText();
        }

        private void UpdateConnectionStatusText()
        {
            ConnectionStatus = IsConnected ? $"Підключено як {Nickname}" : "Не підключено";
        }

        private void SetConnectionState(bool newIsConnectedStatus)
        {
            if (_isConnected == newIsConnectedStatus) return;
            IsConnected = newIsConnectedStatus;

            if (!newIsConnectedStatus)
            { OnlineUsers.Clear(); AddSystemMessageToChat("З'єднання з сервером розірвано або не встановлено."); }
            else { AddSystemMessageToChat("Успішно підключено до сервера!"); }
        }

        private async Task ConnectToServerAsync()
        {
            if (string.IsNullOrWhiteSpace(Nickname)) { AddSystemMessageToChat("Будь ласка, введіть нікнейм."); return; }
            string connectingStatus = $"Підключення до {_serverIp}:{_serverPort} як {Nickname}...";
            System.Windows.Application.Current.Dispatcher.Invoke(() => { if (ConnectionStatus != connectingStatus) ConnectionStatus = connectingStatus; });
            ChatMessages.Clear(); OnlineUsers.Clear();
            await _tcpClientService.ConnectAsync(_serverIp, _serverPort, Nickname);
        }

        private async Task SendMessageAsync()
        {
            Debug.WriteLine("[ViewModel.SendMessageAsync] Початок");
            var messageToActuallySend = MessageToSend?.Trim(); // Додамо ?. для безпеки

            // Очищуємо поле ДО надсилання, але RaiseCanExecuteChanged НЕ викликається з сеттера MessageToSend
            MessageToSend = string.Empty;

            if (string.IsNullOrWhiteSpace(messageToActuallySend))
            {
                Debug.WriteLine("[ViewModel.SendMessageAsync] Повідомлення порожнє, вихід.");
                // Оновлюємо стан кнопки SendCommand тут, оскільки він міг змінитися через очищення MessageToSend
                ((RelayCommand)SendCommand).RaiseCanExecuteChanged();
                return;
            }

            try
            {
                Debug.WriteLine($"[ViewModel.SendMessageAsync] Надсилання: '{messageToActuallySend}'");
                await _tcpClientService.SendMessageAsync(messageToActuallySend);
                Debug.WriteLine($"[ViewModel.SendMessageAsync] Повідомлення надіслано, додавання до ChatMessages.");
                ChatMessages.Add(new Message { Text = $"Ви: {messageToActuallySend}", Timestamp = DateTime.Now, Sender = Nickname, IsOwnMessage = true });
                Debug.WriteLine($"[ViewModel.SendMessageAsync] Повідомлення додано до ChatMessages.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ViewModel.SendMessageAsync] ПОМИЛКА: {ex.Message}");
                AddSystemMessageToChat($"Помилка надсилання: {ex.Message}");
            }
            finally
            {
                // Оновлюємо стан кнопки SendCommand ПІСЛЯ всіх операцій
                ((RelayCommand)SendCommand).RaiseCanExecuteChanged();
                Debug.WriteLine("[ViewModel.SendMessageAsync] Кінець, SendCommand.RaiseCanExecuteChanged викликано.");
            }
        }

        public async Task SendFileAsync() { /* ... код SendFileAsync без змін, що викликають рекурсію ... */ await Task.CompletedTask; }
        private async Task DisconnectFromServerAsync() { await _tcpClientService.DisconnectAsync(); }
        private void OnMessageReceived(ChatMessage receivedObject) { Task.Run(async () => await OnMessageReceivedAsync(receivedObject)); }
        private async Task OnMessageReceivedAsync(ChatMessage receivedObject) { /* ... код обробки як раніше ... */ await Task.CompletedTask; }

        private void OnConnectionStatusChanged(bool newIsConnectedStatus)
        {
            Debug.WriteLine($"[ViewModel] OnConnectionStatusChanged викликано з: {newIsConnectedStatus}. Поточний IsConnected (поле): {_isConnected}");
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                SetConnectionState(newIsConnectedStatus);
            });
        }
        private void OnUserListReceived(List<string> users) { /* ... як раніше ... */ }
        private void AddSystemMessageToChat(string text) { /* ... як раніше ... */ }
        private string FormatFileSize(long bytes) { /* ... як раніше ... */ return string.Empty; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        private class FileReceptionState { /* ... як раніше ... */ }
    }

    public class RelayCommand : ICommand
    { /* ... як раніше ... */
        private readonly Func<object, Task> _executeAsync; private readonly Action<object> _executeSync; private readonly Predicate<object> _canExecute; private readonly bool _isAsync;
        public RelayCommand(Action<object> execute, Predicate<object> canExecute = null) { _executeSync = execute ?? throw new ArgumentNullException(nameof(execute)); _canExecute = canExecute; _isAsync = false; }
        public RelayCommand(Func<object, Task> executeAsync, Predicate<object> canExecute = null) { _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync)); _canExecute = canExecute; _isAsync = true; }
        public bool CanExecute(object parameter) => _canExecute == null || _canExecute(parameter);
        public async void Execute(object parameter) { if (!CanExecute(parameter)) return; if (_isAsync) await _executeAsync(parameter); else _executeSync(parameter); }
        public event EventHandler CanExecuteChanged { add { CommandManager.RequerySuggested += value; } remove { CommandManager.RequerySuggested -= value; } }
        public void RaiseCanExecuteChanged() { CommandManager.InvalidateRequerySuggested(); }
    }
}