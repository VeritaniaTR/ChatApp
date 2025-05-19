// Файл: ChatApp.Client/Models/Message.cs
using System;
using System.IO; // Потрібно для Path.GetFileName
using System.Windows.Media.Imaging;

namespace ChatApp.Client.Models
{
    public class Message
    {
        public string Text { get; set; }
        public DateTime Timestamp { get; set; }
        public BitmapImage Image { get; set; }
        public bool IsImage { get; set; }

        private string _filePath;
        private string fileNameFromPath;

        public string FilePath
        {
            get => _filePath;
            set
            {
                _filePath = value;
                // Автоматично встановлюємо FileNameFromPath, якщо FilePath встановлено
                FileNameFromPath = !string.IsNullOrEmpty(_filePath) ? Path.GetFileName(_filePath) : string.Empty;
            }
        }
        // Ось ця властивість потрібна XAML
        public string FileNameFromPath { get => fileNameFromPath; private set => fileNameFromPath = value; }

        public string Sender { get; set; }
        public bool IsOwnMessage { get; set; }
        public bool IsSystemMessage { get; set; }
    }
}