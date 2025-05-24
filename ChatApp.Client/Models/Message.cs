using System;
using System.IO;
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
        private string _fileNameFromPath; 

        public string FilePath
        {
            get => _filePath;
            set
            {
                _filePath = value;
                // Automatically set FileNameFromPath when FilePath is set
                FileNameFromPath = !string.IsNullOrEmpty(_filePath) ? Path.GetFileName(_filePath) : string.Empty;
            }
        }
        // Это свойство для XAML
        public string FileNameFromPath { get => _fileNameFromPath; private set => _fileNameFromPath = value; }

        public string Sender { get; set; }
        public bool IsOwnMessage { get; set; }
        public bool IsSystemMessage { get; set; }
    }
}