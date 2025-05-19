using System;
using Newtonsoft.Json; // Додайте цей using

namespace ChatApp.Common.Models
{
    public enum MessageType
    {
        ChatMessage,
        SystemMessage,
        UserList,
        Disconnect,
        PrivateMessage,
        FileTransferMetadata, // Новий тип: метадані файлу (початок передачі)
        FileTransferChunk,    // Новий тип: фрагмент файлу
        FileTransferEnd,      // Новий тип: кінець передачі файлу
        FileTransfer,         // <-- ДОДАНО ЦЕЙ ТИП
        TypingStatus
    }

    public class ChatMessage
    {
        [JsonProperty("type")]
        public MessageType Type { get; set; }

        [JsonProperty("sender")]
        public string Sender { get; set; }

        [JsonProperty("recipient")]
        public string Recipient { get; set; } // Для приватних повідомлень

        [JsonProperty("content")]
        public string Content { get; set; } // Текст повідомлення або текстові метадані

        [JsonProperty("timestamp")]
        public DateTime Timestamp { get; set; }

        // Поля для передачі файлів
        [JsonProperty("fileId")] // Унікальний ідентифікатор для сесії передачі файлу
        public Guid FileId { get; set; }

        [JsonProperty("fileName")]
        public string FileName { get; set; }

        [JsonProperty("fileSize")]
        public long FileSize { get; set; }

        [JsonProperty("fileMimeType")]
        public string FileMimeType { get; set; } // Тип файлу (наприклад, "image/jpeg", "application/pdf")

        [JsonProperty("chunkIndex")] // Індекс поточного фрагмента
        public int ChunkIndex { get; set; }

        [JsonProperty("totalChunks")] // Загальна кількість фрагментів
        public int TotalChunks { get; set; }

        [JsonProperty("fileData")] // Сам фрагмент файлу (як масив байтів Base64)
        public string FileData { get; set; }

        public ChatMessage()
        {
            Timestamp = DateTime.Now; // Встановлюємо поточний час за замовчуванням
            FileId = Guid.Empty; // Ініціалізуємо Guid
        }

        public string ToJson()
        {
            return JsonConvert.SerializeObject(this);
        }

        public static ChatMessage FromJson(string json)
        {
            return JsonConvert.DeserializeObject<ChatMessage>(json);
        }
    }
}