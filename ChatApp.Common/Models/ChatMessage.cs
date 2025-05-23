using System;
using Newtonsoft.Json;

namespace ChatApp.Common.Models
{
    public enum MessageType
    {
        ChatMessage,        // Звичайне текстове повідомлення
        SystemMessage,      // Системне повідомлення від сервера або клієнта
        UserList,           // Список користувачів онлайн
        Disconnect,         // Повідомлення про відключення
        PrivateMessage,     // Приватне повідомлення (поки не реалізовано)

        FileTransferMetadata, // Метадані файлу (початок "живої" передачі)
        FileTransferChunk,    // Фрагмент файлу ("жива" передача)
        FileTransferEnd,      // Кінець "живої" передачі файлу

        HistoricFileMessage, // НОВИЙ ТИП: Повідомлення в історії, що представляє файл

        TypingStatus        // Статус набору тексту (поки не реалізовано)
        // FileTransfer був зайвим, видаляємо, якщо не використовується для чогось специфічного
    }

    public class ChatMessage
    {
        [JsonProperty("type")]
        public MessageType Type { get; set; }

        [JsonProperty("sender")]
        public string Sender { get; set; }

        [JsonProperty("recipient")]
        public string Recipient { get; set; }

        [JsonProperty("content")]
        public string Content { get; set; } // Для ChatMessage, SystemMessage. Для HistoricFileMessage може бути порожнім або містити опис.

        [JsonProperty("timestamp")]
        public DateTime Timestamp { get; set; }

        // Поля для передачі файлів та для HistoricFileMessage
        [JsonProperty("fileId")]
        public Guid FileId { get; set; } // Може бути корисним для HistoricFileMessage для ідентифікації

        [JsonProperty("fileName")]
        public string FileName { get; set; }

        [JsonProperty("fileSize")]
        public long FileSize { get; set; }

        [JsonProperty("fileMimeType")]
        public string FileMimeType { get; set; }

        // Ці поля більше для "живої" передачі, але можуть бути у HistoricFileMessage, якщо потрібно
        [JsonProperty("chunkIndex")]
        public int ChunkIndex { get; set; }

        [JsonProperty("totalChunks")]
        public int TotalChunks { get; set; }

        [JsonProperty("fileData")] // Для "живої" передачі, для HistoricFileMessage буде null
        public string FileData { get; set; }

        public ChatMessage()
        {
            Timestamp = DateTime.UtcNow; // Встановлюємо UTC час за замовчуванням
            FileId = Guid.Empty;
        }

        public string ToJson()
        {
            return JsonConvert.SerializeObject(this);
        }

        public static ChatMessage FromJson(string json)
        {
            try
            {
                return JsonConvert.DeserializeObject<ChatMessage>(json);
            }
            catch (Exception ex)
            {
                // Можна додати логування помилки десеріалізації тут
                System.Diagnostics.Debug.WriteLine($"Помилка десеріалізації ChatMessage: {ex.Message} | JSON: {json}");
                return null;
            }
        }
    }
}