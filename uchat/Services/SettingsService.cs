using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Windows.Storage;

namespace uchat.Services
{
    public enum NotificationMode
    {
        All,        // ALL MESSAGES
        Mentions,   // ONLY @
        None        // NONE
    }
    public class UserSettings
    {
        public bool IsLightTheme { get; set; } = false;
        public bool IsAutoDownloadEnabled { get; set; } = false;
        public string AutoDownloadPath { get; set; } = "";
        public string AutoDownloadToken { get; set; } = "";
        public NotificationMode NotifyMode { get; set; } = NotificationMode.All;
    }

    public static class SettingsService
    {
        private const string FolderName = "uchat_client";
        private const string FileName = "client_settings.json";
        private static Dictionary<string, UserSettings> _cache = new();
        private static string _folderPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FolderName);
        public static string FilePath => Path.Combine(_folderPath, FileName);

        public static void Load()
        {
            try
            {
                if (!Directory.Exists(_folderPath))
                {
                    Directory.CreateDirectory(_folderPath);
                    System.Diagnostics.Debug.WriteLine($"[Settings] Created folder at: {_folderPath}");
                }
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    _cache = JsonSerializer.Deserialize<Dictionary<string, UserSettings>>(json) ?? new();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Settings] Load error: {ex.Message}");
                _cache = new();
            }
        }

        public static UserSettings GetForUser(string username)
        {
            if (string.IsNullOrEmpty(username)) return new UserSettings();

            if (!_cache.ContainsKey(username))
            {
                _cache[username] = new UserSettings();
            }
            return _cache[username];
        }

        public static void SaveForUser(string username, UserSettings settings)
        {
            if (string.IsNullOrEmpty(username)) return;
            _cache[username] = settings;
            SaveToFile();
        }

        private static void SaveToFile()
        {
            try
            {
                var json = JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
                System.Diagnostics.Debug.WriteLine($"Settings Path: {FilePath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving settings: {ex.Message}");
            }
        }

        public static void DeleteUser(string username)
        {
            if (string.IsNullOrEmpty(username)) return;

            if (_cache.ContainsKey(username))
            {
                _cache.Remove(username);
                SaveToFile(); 
                System.Diagnostics.Debug.WriteLine($"[Settings] Deleted settings for user: {username}");
            }
        }

        public static void RenameUser(string oldUsername, string newUsername)
        {
            if (string.IsNullOrEmpty(oldUsername) || string.IsNullOrEmpty(newUsername)) return;
            if (oldUsername == newUsername) return;
            if (_cache.ContainsKey(oldUsername))
            {
                var settings = _cache[oldUsername];
                _cache.Remove(oldUsername);
                _cache[newUsername] = settings;
                SaveToFile();
                System.Diagnostics.Debug.WriteLine($"[Settings] Renamed settings from {oldUsername} to {newUsername}");
            }
        }

    }
}