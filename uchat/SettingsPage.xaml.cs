using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Runtime.InteropServices; // Для работы с окнами
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;
using uchat.Services;
namespace uchat
{
    public sealed partial class SettingsPage : Page
    {
        public string CurrentUsername { get; set; } = string.Empty;
        private UserSettings _currentSettings = new();
        private bool _isLoadingSettings = false;
        private const string DownloadFolderToken = "ChatDownloadFolder";
        public static event EventHandler<bool>? ThemeChanged;
        public static event EventHandler? LogoutRequested;
        public static event EventHandler? DeleteAccountRequested;
        public static event EventHandler<(string oldPass, string newPass)>? ChangePasswordRequested;
        private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        private const string AutoDownloadTokenKey = "ChatAutoDownloadToken";
        private const string AutoDownloadPathKey = "ChatAutoDownloadPath";
        private const string AutoDownloadEnabledKey = "IsAutoDownloadEnabled";
        public SettingsPage()
        {
            this.InitializeComponent();
            ApplyMicaBackdrop();
        }

        private void ApplyMicaBackdrop()
        {
            try { if (RootGrid != null) RootGrid.Background = new SolidColorBrush(Colors.Transparent); } catch { }
        }

        private string GetKey(string baseKey)
        {
            return string.IsNullOrEmpty(CurrentUsername) ? baseKey : $"{CurrentUsername}_{baseKey}";
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is string username && !string.IsNullOrEmpty(username))
            {
                CurrentUsername = username;
                _currentSettings = SettingsService.GetForUser(CurrentUsername);
                ApplySettingsToUI();
            }
        }

        private void ApplySettingsToUI()
        {
            _isLoadingSettings = true; 
            try
            {
                UpdateThemeButtonContent();
                UpdateThemeColors();
                if (AutoDownloadToggle != null)
                    AutoDownloadToggle.IsOn = _currentSettings.IsAutoDownloadEnabled;
                if (FolderPathText != null)
                    FolderPathText.Text = string.IsNullOrEmpty(_currentSettings.AutoDownloadPath)
                        ? "No folder selected"
                        : _currentSettings.AutoDownloadPath;
                if (PickFolderButton != null && AutoDownloadToggle != null)
                    PickFolderButton.IsEnabled = AutoDownloadToggle.IsOn;
                if (NotificationModeBox != null)
                {
                    NotificationModeBox.SelectedIndex = (int)_currentSettings.NotifyMode;
                }
            }
            finally
            {
                _isLoadingSettings = false;
            }
        }

        public void EnablePasswordButton()
        {
            if (ChangePasswordButton != null)
            {
                ChangePasswordButton.IsEnabled = true;
                ChangePasswordButton.Content = "Update Password";
            }
        }

        public void ResetPasswordFields()
        {
            if (OldPasswordBox != null) OldPasswordBox.Password = "";
            if (NewPasswordBox != null) NewPasswordBox.Password = "";
            if (ConfirmPasswordBox != null) ConfirmPasswordBox.Password = "";

            if (ChangePasswordButton != null)
            {
                ChangePasswordButton.IsEnabled = true;
                ChangePasswordButton.Content = "Update Password";
            }
        }

        private async void ChangePasswordButton_Click(object sender, RoutedEventArgs e)
        {
            var oldPass = OldPasswordBox.Password;
            var newPass = NewPasswordBox.Password;
            var confirmPass = ConfirmPasswordBox.Password;

            if (string.IsNullOrEmpty(oldPass) || string.IsNullOrEmpty(newPass))
            {
                ShowDebugAlert("Please fill in all fields.");
                return;
            }

            if (newPass != confirmPass)
            {
                ShowDebugAlert("New passwords do not match.");
                return;
            }

            if (newPass.Length < 6) 
            {
                ShowDebugAlert("New password is too short.");
                return;
            }

            ChangePasswordButton.IsEnabled = false;
            ChangePasswordButton.Content = "Updating...";

            ChangePasswordRequested?.Invoke(this, (oldPass, newPass));
        }

        private async void PickFolder_Click(object sender, RoutedEventArgs e)
        {
            IntPtr hwnd = IntPtr.Zero;
            try
            {
                if (App.m_window == null) return;
                hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);
            }
            catch { return; }

            try
            {
                var folderPicker = new FolderPicker();
                WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);

                folderPicker.SuggestedStartLocation = PickerLocationId.Downloads;
                folderPicker.FileTypeFilter.Add("*");

                var folder = await folderPicker.PickSingleFolderAsync();

                if (folder != null)
                {
                    string token = "";

                    try
                    {
                        try { Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.Clear(); } catch { }

                        string tokenId = "AutoDownload_" + Guid.NewGuid().ToString().Substring(0, 8);

                        Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.AddOrReplace(tokenId, folder);

                        token = tokenId; 
                    }
                    catch (Exception tokenEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Warning: Could not create Access Token: {tokenEx.Message}");
                        token = "";
                    }

                    _currentSettings.AutoDownloadToken = token;
                    _currentSettings.AutoDownloadPath = folder.Path;

                    if (FolderPathText != null) FolderPathText.Text = folder.Path;

                    if (!string.IsNullOrEmpty(CurrentUsername))
                    {
                        SettingsService.SaveForUser(CurrentUsername, _currentSettings);
                    }
                }
            }
            catch (Exception ex)
            {
                ShowDebugAlert($"Помилка вибору папки: {ex.Message}");
            }
        }

        private async void ShowDebugAlert(string content)
        {
            try
            {
                if (this.XamlRoot != null)
                {
                    var dialog = new ContentDialog
                    {
                        Title = "Діагностика",
                        Content = content,
                        PrimaryButtonText = "OK",
                        XamlRoot = this.XamlRoot
                    };
                    await dialog.ShowAsync();
                }
            }
            catch { }
        }

        private void AutoDownloadToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings || AutoDownloadToggle == null) return;
            _currentSettings.IsAutoDownloadEnabled = AutoDownloadToggle.IsOn;
            if (PickFolderButton != null)
                PickFolderButton.IsEnabled = AutoDownloadToggle.IsOn;

            SettingsService.SaveForUser(CurrentUsername, _currentSettings);
        }

        private void UpdateThemeButtonContent()
        {
            if (ThemeIcon != null) ThemeIcon.Glyph = _currentSettings.IsLightTheme ? "\uE706" : "\uE708";
            if (ThemeText != null) ThemeText.Text = _currentSettings.IsLightTheme ? "Light Mode" : "Dark Mode";
        }

        private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _currentSettings.IsLightTheme = !_currentSettings.IsLightTheme;
            UpdateThemeButtonContent();
            UpdateThemeColors();
            SettingsService.SaveForUser(CurrentUsername, _currentSettings); 
            ThemeChanged?.Invoke(this, _currentSettings.IsLightTheme);
        }

        private void UpdateThemeColors()
        {
            var appResources = this.Resources;
            Color GetColor(string key)
            {
                if (appResources.TryGetValue(key, out object resource) && resource is Color color) return color;
                return Colors.Black;
            }

            var prefix = _currentSettings.IsLightTheme ? "Light_" : "Dark_";
            var keys = new[] { "BackgroundDarkerColor", "CardBackgroundColor", "TextWhiteColor", "TextGrayColor", "BorderDarkColor" };

            foreach (var key in keys)
            {
                var targetColor = GetColor(prefix + key);
                string currentBrushKey = key switch
                {
                    "BackgroundDarkerColor" => "Current_BackgroundDarker",
                    "CardBackgroundColor" => "Current_CardBackground",
                    "TextWhiteColor" => "Current_TextWhite",
                    "TextGrayColor" => "Current_TextGray",
                    "BorderDarkColor" => "Current_BorderDark",
                    _ => ""
                };
                if (!string.IsNullOrEmpty(currentBrushKey) && appResources.TryGetValue(currentBrushKey, out object brushResource) && brushResource is SolidColorBrush brush)
                {
                    brush.Color = targetColor;
                }
            }
        }

        private async void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Log Out",
                Content = "Are you sure you want to log out?",
                PrimaryButtonText = "Log Out",
                SecondaryButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Secondary,
                XamlRoot = this.XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                LogoutRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        private async void DeleteAccountButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Delete Account",
                Content = "Are you sure you want to delete your account? This action cannot be undone. All your messages and data will be permanently deleted.",
                PrimaryButtonText = "Delete",
                SecondaryButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Secondary,
                XamlRoot = this.XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                DeleteAccountRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        private void NotificationModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingSettings || NotificationModeBox == null) return;
            _currentSettings.NotifyMode = (NotificationMode)NotificationModeBox.SelectedIndex;
            SettingsService.SaveForUser(CurrentUsername, _currentSettings);
        }

        public void SaveAllSettings()
        {
        }
    }
}
