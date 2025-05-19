using System;
using System.Windows.Media.Imaging;

namespace ChatApp.Client.Models
{
    public class Message
    {
        public string Text { get; set; }
        public DateTime Timestamp { get; set; }
        public BitmapImage Image { get; set; }
        public bool IsImage { get; set; }
        public string FilePath { get; set; }
        public string Sender { get; set; }
        public bool IsOwnMessage { get; set; } // НОВИЙ РЯДОК: для вирівнювання повідомлень
        public bool IsSystemMessage { get; set; } // НОВИЙ РЯДОК: для спеціального форматування системних повідомлень
    }
}