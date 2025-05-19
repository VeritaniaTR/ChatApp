using System;
using System.Windows.Media.Imaging; // Додайте цей using

namespace ChatApp.Client.Models
{
    public class Message
    {
        public string Text { get; set; }
        public DateTime Timestamp { get; set; }
        public BitmapImage Image { get; set; } // Для відображення зображень
        public bool IsImage { get; set; } // Індикатор, чи це зображення
        public string FilePath { get; set; } // Шлях до збереженого файлу (для інших типів)
        public string Sender { get; set; } // Додана властивість для відправника
    }
}