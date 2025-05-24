using System;
using System.Collections.Generic;
using ChatApp.Common.Models;
using Microsoft.Data.Sqlite;
using System.IO;

namespace ChatApp.Server.Core
{
    public class ChatDatabase
    {
        private readonly string _connectionString;

        public ChatDatabase(string dbFileName = "chat_history.db")
        {
            string baseDirectory = AppContext.BaseDirectory;
            _connectionString = $"Data Source={Path.Combine(baseDirectory, dbFileName)}";
            Console.WriteLine($"Database path: {_connectionString}");
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Messages (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Timestamp TEXT NOT NULL,          
                        Sender TEXT NOT NULL,
                        Content TEXT,                     
                        MessageType INTEGER NOT NULL,     
                        Recipient TEXT,                   
                        
                        FileId TEXT,                      
                        FileName TEXT,
                        FileSize INTEGER,                 
                        FileMimeType TEXT
                    );";
                command.ExecuteNonQuery();

                // Проверка для простой миграции
                // это если база есть со старой схемой
                AddFieldIfNotExists(connection, "Messages", "Recipient", "TEXT");
                AddFieldIfNotExists(connection, "Messages", "FileId", "TEXT");
                AddFieldIfNotExists(connection, "Messages", "FileName", "TEXT");
                AddFieldIfNotExists(connection, "Messages", "FileSize", "INTEGER");
                AddFieldIfNotExists(connection, "Messages", "FileMimeType", "TEXT");
            }
        }

        private void AddFieldIfNotExists(SqliteConnection connection, string tableName, string columnName, string columnType)
        {
            try
            {
                var command = connection.CreateCommand();
                // A more robust way to check if a column exists is PRAGMA table_info(tableName)
                // This is a simplified check.
                command.CommandText = $"SELECT {columnName} FROM {tableName} LIMIT 1;";
                command.ExecuteScalar(); // Using ExecuteScalar which might be better for a single value check or existence.
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 1 && ex.Message.ToLower().Contains($"no such column: {columnName.ToLower()}"))
            {
                var alterCmd = connection.CreateCommand();
                alterCmd.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnType};";
                try
                {
                    alterCmd.ExecuteNonQuery();
                    Console.WriteLine($"Column '{columnName}' added to table '{tableName}'.");
                }
                catch (Exception alterEx)
                {
                    Console.WriteLine($"Error adding column '{columnName}': {alterEx.Message}");
                }
            }
            catch (Exception) { /* Column likely exists, or other error */ }
        }


        public void SaveMessage(ChatMessage message)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO Messages (Timestamp, Sender, Content, MessageType, Recipient, FileId, FileName, FileSize, FileMimeType) 
                    VALUES ($timestamp, $sender, $content, $messageType, $recipient, $fileId, $fileName, $fileSize, $fileMimeType)";

                command.Parameters.AddWithValue("$timestamp", new DateTimeOffset(message.Timestamp.ToUniversalTime()).ToString("o"));
                command.Parameters.AddWithValue("$sender", message.Sender);
                command.Parameters.AddWithValue("$content", (object)message.Content ?? DBNull.Value);
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