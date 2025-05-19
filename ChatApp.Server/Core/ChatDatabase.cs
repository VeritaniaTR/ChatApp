using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite; // Не забудьте додати NuGet-пакет!

namespace ChatApp.Server.Core
{
    public class ChatDatabase
    {
        private readonly string _connectionString;

        public ChatDatabase(string dbFileName = "chat_history.db")
        {
            _connectionString = $"Data Source={dbFileName}";
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
                        Content TEXT NOT NULL
                    );";
                command.ExecuteNonQuery();
            }
        }

        public void SaveMessage(DateTime timestamp, string sender, string content)
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText =
                    "INSERT INTO Messages (Timestamp, Sender, Content) VALUES ($timestamp, $sender, $content)";
                command.Parameters.AddWithValue("$timestamp", timestamp.ToString("o")); // ISO 8601 формат
                command.Parameters.AddWithValue("$sender", sender);
                command.Parameters.AddWithValue("$content", content);
                command.ExecuteNonQuery();
            }
        }

        public List<string> GetMessageHistory(int limit = 100)
        {
            List<string> history = new List<string>();
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = $"SELECT Timestamp, Sender, Content FROM Messages ORDER BY Id ASC LIMIT {limit}";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string timestamp = reader.GetString(0);
                        string sender = reader.GetString(1);
                        string content = reader.GetString(2);
                        history.Add($"[{DateTime.Parse(timestamp).ToString("HH:mm:ss")}] {sender}: {content}");
                    }
                }
            }
            return history;
        }
    }
}