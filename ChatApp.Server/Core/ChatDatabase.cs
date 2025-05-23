using System;
using System.Collections.Generic;
using ChatApp.Common.Models;
using Microsoft.Data.Sqlite;
using System.IO; // Для Path

namespace ChatApp.Server.Core
{
    public class ChatDatabase
    {
        private readonly string _connectionString;

        public ChatDatabase(string dbFileName = "chat_history.db")
        {
            // Переконаємося, що база даних зберігається в папці програми
            string baseDirectory = AppContext.BaseDirectory;
            _connectionString = $"Data Source={Path.Combine(baseDirectory, dbFileName)}";
            Console.WriteLine($"Шлях до БД: {_connectionString}");
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                // Оновлена схема таблиці
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Messages (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Timestamp TEXT NOT NULL,          -- Зберігаємо як ISO 8601 DateTimeOffset рядок
                        Sender TEXT NOT NULL,
                        Content TEXT,                     -- Може бути NULL для файлових повідомлень
                        MessageType INTEGER NOT NULL,     -- Тип повідомлення з enum MessageType
                        Recipient TEXT,                   -- Для приватних повідомлень
                        
                        -- Поля для файлів
                        FileId TEXT,                      -- Guid як текст
                        FileName TEXT,
                        FileSize INTEGER,                 -- long
                        FileMimeType TEXT
                    );";
                command.ExecuteNonQuery();

                // Перевірка та додавання колонок, якщо їх немає (проста міграція)
                // Це потрібно, якщо база даних вже існує зі старою схемою
                AddFieldIfNotExists(connection, "Messages", "Recipient", "TEXT");
                AddFieldIfNotExists(connection, "Messages", "FileId", "TEXT");
                AddFieldIfNotExists(connection, "Messages", "FileName", "TEXT");
                AddFieldIfNotExists(connection, "Messages", "FileSize", "INTEGER");
                AddFieldIfNotExists(connection, "Messages", "FileMimeType", "TEXT");
                // Переконуємося, що MessageType має значення за замовчуванням, якщо раніше його не було
                // Ця логіка може бути складнішою для існуючих даних, але для нових колонок це не так критично
            }
        }

        // Допоміжний метод для додавання колонки, якщо вона не існує
        private void AddFieldIfNotExists(SqliteConnection connection, string tableName, string columnName, string columnType)
        {
            try
            {
                var command = connection.CreateCommand();
                command.CommandText = $"SELECT {columnName} FROM {tableName} LIMIT 1;";
                command.ExecuteNonQuery(); // Якщо колонка існує, це не викличе помилку (або викличе, якщо таблиця порожня)
                                           // Краще перевіряти через PRAGMA table_info(tableName)
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 1) // SQLITE_ERROR (no such column)
            {
                var alterCmd = connection.CreateCommand();
                alterCmd.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnType};";
                try
                {
                    alterCmd.ExecuteNonQuery();
                    Console.WriteLine($"Колонку '{columnName}' додано до таблиці '{tableName}'.");
                }
                catch (Exception alterEx)
                {
                    Console.WriteLine($"Помилка додавання колонки '{columnName}': {alterEx.Message}");
                }
            }
            catch (Exception) { /* Колонка, ймовірно, існує, або інша помилка */ }
        }


        // Оновлений метод збереження
        public void SaveMessage(ChatMessage message)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO Messages (Timestamp, Sender, Content, MessageType, Recipient, FileId, FileName, FileSize, FileMimeType) 
                    VALUES ($timestamp, $sender, $content, $messageType, $recipient, $fileId, $fileName, $fileSize, $fileMimeType)";

                command.Parameters.AddWithValue("$timestamp", new DateTimeOffset(message.Timestamp.ToUniversalTime()).ToString("o")); // Зберігаємо в UTC ISO 8601
                command.Parameters.AddWithValue("$sender", message.Sender);
                command.Parameters.AddWithValue("$content", (object)message.Content ?? DBNull.Value); // Дозволяємо NULL
                command.Parameters.AddWithValue("$messageType", (int)message.Type);
                command.Parameters.AddWithValue("$recipient", (object)message.Recipient ?? DBNull.Value);

                command.Parameters.AddWithValue("$fileId", message.FileId != Guid.Empty ? message.FileId.ToString() : (object)DBNull.Value);
                command.Parameters.AddWithValue("$fileName", (object)message.FileName ?? DBNull.Value);
                command.Parameters.AddWithValue("$fileSize", message.FileSize > 0 ? (object)message.FileSize : DBNull.Value);
                command.Parameters.AddWithValue("$fileMimeType", (object)message.FileMimeType ?? DBNull.Value);

                command.ExecuteNonQuery();
            }
        }

        public List<ChatMessage> GetMessageHistory(int limit = 50)
        {
            List<ChatMessage> history = new List<ChatMessage>();
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = $@"
                    SELECT Timestamp, Sender, Content, MessageType, Recipient, FileId, FileName, FileSize, FileMimeType 
                    FROM (SELECT * FROM Messages ORDER BY Id DESC LIMIT {limit}) 
                    ORDER BY Id ASC;";

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var msg = new ChatMessage
                        {
                            Timestamp = DateTimeOffset.Parse(reader.GetString(0)).UtcDateTime,
                            Sender = reader.GetString(1),
                            Content = reader.IsDBNull(2) ? null : reader.GetString(2),
                            Type = (MessageType)reader.GetInt32(3),
                            Recipient = reader.IsDBNull(4) ? null : reader.GetString(4),
                            FileId = reader.IsDBNull(5) ? Guid.Empty : Guid.Parse(reader.GetString(5)),
                            FileName = reader.IsDBNull(6) ? null : reader.GetString(6),
                            FileSize = reader.IsDBNull(7) ? 0 : reader.GetInt64(7),
                            FileMimeType = reader.IsDBNull(8) ? null : reader.GetString(8)
                        };
                        history.Add(msg);
                    }
                }
            }
            return history;
        }
    }
}