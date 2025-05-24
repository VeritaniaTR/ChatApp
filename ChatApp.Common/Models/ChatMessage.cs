using System;
using Newtonsoft.Json;

namespace ChatApp.Common.Models
{
    public enum MessageType
    {
        ChatMessage,
        SystemMessage,
        UserList,
        Disconnect,
        PrivateMessage,

        FileTransferMetadata,
        FileTransferChunk,
        FileTransferEnd,

        HistoricFileMessage, // представляет файл(не забыть)

        TypingStatus
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
        public string Content { get; set; }

        [JsonProperty("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonProperty("fileId")]
        public Guid FileId { get; set; }

        [JsonProperty("fileName")]
        public string FileName { get; set; }

        [JsonProperty("fileSize")]
        public long FileSize { get; set; }

        [JsonProperty("fileMimeType")]
        public string FileMimeType { get; set; }

        [JsonProperty("chunkIndex")]
        public int ChunkIndex { get; set; }

        [JsonProperty("totalChunks")]
        public int TotalChunks { get; set; }

        [JsonProperty("fileData")]
        public string FileData { get; set; }

        public ChatMessage()
        {
            Timestamp = DateTime.UtcNow;
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
                System.Diagnostics.Debug.WriteLine($"Error deserializing ChatMessage: {ex.Message} | JSON: {json}");
                return null;
            }
        }
    }
}