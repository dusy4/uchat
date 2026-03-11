using CommunityToolkit.WinUI.Controls; 
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using SQLitePCL;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using uchat.Models;
using uchat.Protocol;
using uchat.Services;
using Windows.ApplicationModel.Chat;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using static System.Collections.Specialized.BitVector32;
using CommunityToolkit.WinUI.Notifications; 
namespace uchat
{
    public sealed partial class MainPage : Page
    {
        private NetworkClient? _networkClient;
        private string _currentUsername = string.Empty;
        private string _currentDisplayName = string.Empty;
        private bool _isConnected = false;
        private string _currentBio = "";
        private string _currentColor = "#0088CC";
        private string _currentAvatarBase64 = "";
        private string? _currentChatTarget = null;
        private string? _lastLoadedChat = null;
        private string _storedPassword = "";
        private VoiceMessageManager _voiceManager;
        private string _serverIp = "";
        private int _serverPort = 0;
        bool isAdmin = true;
        private readonly ObservableCollection<MessageViewModel> _messages = new();
        private readonly ObservableCollection<MessageViewModel> _filteredMessages = new();
        private readonly ObservableCollection<UserItemModel> _usersList = new();
        private ObservableCollection<GifPickerItem> _gifPickerItems = new();
        private ObservableCollection<StickerPackModel> _stickerPacks = new();
        private ObservableCollection<StickerItemModel> _currentStickers = new();
        private Dictionary<string, ImageSource> _stickerCache = new();
        private readonly List<UserItemModel> _allUsersCache = new();
        private string _searchQuery = "";
        private GroupProfileDialog? _activeGroupDialog;
        private int _myUserId;
        private ObservableCollection<GifPickerItem> _gifList = new();
        private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;
        private System.Threading.Timer? _heartbeatTimer;
        private bool _isRefreshingUserList = false;
        private bool _isScrolledToBottom = true;
        private bool _hasNewMessages = false;
        private bool _isLightTheme = false;
        private bool _isResizingLeft = false;
        private bool _isResizingRight = false;
        private bool _isLoadingGifs = false;
        private bool _isLoadingHistory = false;
        private bool _isLoadingFuture = false;
        private bool _isFutureFullyLoaded = true;
        private int? _pendingJumpMessageId = null;
        private DispatcherTimer _searchDebounceTimer;
        private MessageViewModel? _replyingToMessage = null;
        private double _initialMouseX = 0;
        private double _initialColumnWidth = 0;
        private bool _isReconnecting = false;
        private Windows.Media.Playback.MediaPlayer? _sharedMediaPlayer;
        private int _currentlyPlayingMessageId = -1;
        private DispatcherTimer? _voicePlaybackTimer;
        private DispatcherTimer? _stickerPreviewTimer;
        private StickerItemModel? _hoveredSticker = null;
        private bool _isDoNotDisturbEnabled = true;
        public static double _defaultVoiceVolume = 100;
        private double _baseWidth;
        private double _baseHeight;
        private double _currentZoom = 1.0;
        private double _currentZoomFactor = 1.0;
        private const int MIN_WINDOW_W = 940;
        private const int MIN_WINDOW_H = 560;
        private Microsoft.UI.Windowing.AppWindow? _appWindow;
        private int _currentUserId = 0;
        private string _currentGroupAvatarBase64 = "";
        private bool _isCroppingGroup = false;
        private Windows.Storage.StorageFile? _tempCropFile;
        private bool _isUserSeeking = false;
        private bool _isUserDraggingSlider = false;
        private bool _isTimerUpdating = false;
        private static Dictionary<string, BitmapImage> _memoryImageCache = new();
        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        private static Dictionary<string, BitmapImage> _ramGifCache = new();
        [DllImport("user32.dll")]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        private IntPtr _oldWndProc;
        private WndProcDelegate _newWndProc;
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        private bool IsAppInForeground()
        {
            if (App.m_window == null) return false;
            try
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);
                var activeHwnd = GetForegroundWindow();
                return hwnd == activeHwnd;
            }
            catch { return false; }
        }
        private const int GWLP_WNDPROC = -4;
        private const int WM_GETMINMAXINFO = 0x0024;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        private void RegisterMinSizeHook()
        {
            if (App.m_window == null) return;

            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);
            _newWndProc = new WndProcDelegate(CustomWndProc);
            _oldWndProc = SetWindowLongPtr(hWnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_newWndProc));
        }

        private IntPtr CustomWndProc(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam)
        {
            if (Msg == WM_GETMINMAXINFO)
            {
                var minMaxInfo = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                minMaxInfo.ptMinTrackSize.x = 940;
                minMaxInfo.ptMinTrackSize.y = 560;

                Marshal.StructureToPtr(minMaxInfo, lParam, true);
            }
            return CallWindowProc(_oldWndProc, hWnd, Msg, wParam, lParam);
        }

        public MainPage()
        {
            this.InitializeComponent();
            uchat.Services.SettingsService.Load();
            _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            this.SizeChanged += (s, e) => UpdateInterfaceLayout();
            MessagesListView.ItemsSource = _filteredMessages;
            UsersListView.ItemsSource = _usersList;
            
            _usersList.CollectionChanged += UsersList_CollectionChanged;
            
            _voiceManager = new VoiceMessageManager();
            MessagesListView.Loaded += MessagesListView_Loaded;
            InitializeBaseSize();
            InitializeWindowControl();
            EmojiGrid.ItemsSource = _gifList;
            AppViewbox.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(OnZoom), true);
            this.SizeChanged += (s, e) => UpdateInterfaceSize(e.NewSize.Width, e.NewSize.Height);
            LoadLogo();
            LoadTitleBarLogo();
            this.SizeChanged += MainPage_SizeChanged;
            SettingsPage.ThemeChanged += SettingsPage_ThemeChanged;
            SettingsPage.LogoutRequested += SettingsPage_LogoutRequested;
            SettingsPage.DeleteAccountRequested += SettingsPage_DeleteAccountRequested;
            SettingsPage.ChangePasswordRequested += SettingsPage_ChangePasswordRequested;
            this.MessagesListView.Loaded += (s, e) =>
            {
                var scrollViewer = FindScrollViewer(MessagesListView);
                if (scrollViewer != null)
                {
                    scrollViewer.ViewChanged += MessagesScrollViewer_ViewChanged;
                }
            };
            this.Loaded += (s, e) => 
            {
                RegisterMinSizeHook();
                UpdateMessageTextBoxHeight();
            };
            UpdatePageBackground();
            _searchDebounceTimer = new DispatcherTimer();
            _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(600); 
            _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;
            if (App.m_window != null)
            {
                try
                {
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);
                    var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                    var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
                }
                catch { }
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is ValueTuple<string, int> connectionConfig)
            {
                _serverIp = connectionConfig.Item1;
                _serverPort = connectionConfig.Item2;
                System.Diagnostics.Debug.WriteLine($"[Client] Configured via CLI: {_serverIp}:{_serverPort}");
                if (App.m_window != null)
                    App.m_window.Title = $"Chat - Target: {_serverIp}:{_serverPort}";
            }
            else if (e.Parameter is string username && !string.IsNullOrEmpty(username))
            {
                _currentUsername = username;
            }
            if (string.IsNullOrEmpty(_serverIp) || _serverPort == 0)
            {
                Console.WriteLine("USAGE: uchat.exe <ip> <port>");
                Console.WriteLine("Example: uchat.exe 25.1.2.3 1337");
                Application.Current.Exit();
            }
        }

        private void UpdateInterfaceLayout()
        {
            if (RootGrid == null) return;

            double windowW = this.ActualWidth;
            double windowH = this.ActualHeight;

            if (windowW == 0 || windowH == 0) return;

            RootGrid.Width = windowW / _currentZoom;
            RootGrid.Height = windowH / _currentZoom;
        }
        private void UpdateInterfaceSize(double winW, double winH)
        {
            if (RootGrid == null || winW == 0 || winH == 0) return;

            if (_currentZoom < 0.1) _currentZoom = 0.1;

            RootGrid.Width = winW / _currentZoom;
            RootGrid.Height = winH / _currentZoom;
        }

        private async void MessagesListView_Loaded(object sender, RoutedEventArgs e)
        {
            await Task.Delay(500);
            var scrollViewer = FindScrollViewer(MessagesListView);
            if (scrollViewer != null)
            {
                scrollViewer.ViewChanged -= MessagesScrollViewer_ViewChanged;
                scrollViewer.ViewChanged += MessagesScrollViewer_ViewChanged;
            }
        }

        private async void HookChatScroll()
        {   
            await Task.Delay(500);
            var scrollViewer = FindScrollViewer(MessagesListView);
            if (scrollViewer != null)
            {
                scrollViewer.ViewChanged -= MessagesScrollViewer_ViewChanged;
                scrollViewer.ViewChanged += MessagesScrollViewer_ViewChanged;
            }
        }

        private ScrollViewer? FindScrollViewer(DependencyObject defObj)
        {
            if (defObj is ScrollViewer sv) return sv;
            int childrenCount = VisualTreeHelper.GetChildrenCount(defObj);
            for (int i = 0; i < childrenCount; i++)
            {
                var child = VisualTreeHelper.GetChild(defObj, i);
                var result = FindScrollViewer(child);
                if (result != null) return result;
            }
            return null;
        }
        private void InitializeWindowControl()
        {
            try
            {
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
                _appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
                if (_appWindow.TitleBar != null)
                {
                    _appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
                    UpdateTitleBarColors();
                }
                UpdateInterfaceSize(this.ActualWidth, this.ActualHeight);
            }
            catch { }
        }

        private void UpdateTitleBarColors()
        {
            if (_appWindow != null && _appWindow.TitleBar != null)
            {
                var titleBar = _appWindow.TitleBar;
                titleBar.ButtonBackgroundColor = Colors.Transparent;
                titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
                if (_isLightTheme)
                {
                    titleBar.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 17, 17, 17);
                    titleBar.ButtonInactiveForegroundColor = Colors.DarkGray;
                    titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(30, 0, 0, 0);
                    titleBar.ButtonHoverForegroundColor = Windows.UI.Color.FromArgb(255, 17, 17, 17);
                    titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(60, 0, 0, 0);
                    titleBar.ButtonPressedForegroundColor = Windows.UI.Color.FromArgb(255, 17, 17, 17);
                }
                else
                {
                    titleBar.ButtonForegroundColor = Colors.White;
                    titleBar.ButtonInactiveForegroundColor = Colors.Gray;
                    titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(30, 255, 255, 255);
                    titleBar.ButtonHoverForegroundColor = Colors.White;
                    titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(60, 255, 255, 255);
                    titleBar.ButtonPressedForegroundColor = Colors.White;
                }
            }
        }

        private void UpdatePageBackground()
        {
            try
            {
                if (this.Resources.TryGetValue("Current_BackgroundDark", out object brush) && brush is SolidColorBrush backgroundBrush)
                {
                    this.Background = backgroundBrush;
                }
            }
            catch { }
        }
        private void MainPage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateRootGridSize(e.NewSize);
        }
        private void InitializeBaseSize()
        {
            try
            {
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);

                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);

                var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(windowId, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);

                _baseWidth = displayArea.WorkArea.Width;
                _baseHeight = displayArea.WorkArea.Height;

                if (_baseWidth < 800) _baseWidth = 1280;
                if (_baseHeight < 600) _baseHeight = 720;

                if (RootGrid != null)
                {
                    RootGrid.Width = _baseWidth;
                    RootGrid.Height = _baseHeight;
                }

                System.Diagnostics.Debug.WriteLine($"Detected Screen Resolution: {_baseWidth}x{_baseHeight}");
            }
            catch
            {
                _baseWidth = 1920;
                _baseHeight = 1080;
                if (RootGrid != null)
                {
                    RootGrid.Width = _baseWidth;
                    RootGrid.Height = _baseHeight;
                }
            }
        }
        private void UpdateRootGridSize(Windows.Foundation.Size windowSize)
        {
            if (RootGrid == null) return;

            RootGrid.Width = windowSize.Width / _currentZoomFactor;
            RootGrid.Height = windowSize.Height / _currentZoomFactor;
        }

        private async void SettingsPage_ChangePasswordRequested(object? sender, (string oldPass, string newPass) e)
        {
            if (_networkClient == null || !_networkClient.IsConnected)
            {
                var dialog = new ContentDialog { Title = "Error", Content = "No connection to server.", PrimaryButtonText = "OK", XamlRoot = this.XamlRoot };
                await dialog.ShowAsync();
                return;
            }

            await _networkClient.SendMessageAsync(new ProtocolMessage
            {
                Type = MessageType.ChangePassword,
                Parameters = new Dictionary<string, string>
        {
            { "oldPassword", e.oldPass },
            { "newPassword", e.newPass }
        }
            });
        }


        private async void PlayVoiceMessage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is MessageViewModel msg)
            {
                if (!msg.IsVoice || string.IsNullOrEmpty(msg.VoiceData)) return;

                try
                {
                    if (_sharedMediaPlayer == null)
                    {
                        _sharedMediaPlayer = new Windows.Media.Playback.MediaPlayer();

                        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                        timer.Tick += (s, args) =>
                        {
                            if (_sharedMediaPlayer.PlaybackSession != null && _currentlyPlayingMessageId != -1)
                            {
                                var session = _sharedMediaPlayer.PlaybackSession;
                                var currentMsg = _messages.FirstOrDefault(m => m.Id == _currentlyPlayingMessageId);

                                if (currentMsg != null && session.NaturalDuration.TotalSeconds > 0)
                                {
                                    currentMsg.VoicePosition = session.Position;
                                    currentMsg.VoiceDuration = session.NaturalDuration;

                                    _isTimerUpdating = true;
                                    currentMsg.VoiceProgressValue = (session.Position.TotalSeconds / session.NaturalDuration.TotalSeconds) * 100;
                                    _isTimerUpdating = false; 
                                }
                            }
                        };
                        timer.Start();

                        _sharedMediaPlayer.MediaEnded += (s, args) => {
                            _dispatcherQueue.TryEnqueue(() => {
                                var m = _messages.FirstOrDefault(x => x.Id == _currentlyPlayingMessageId);
                                if (m != null) { m.IsVoicePlaying = false; m.VoiceProgressValue = 0; m.VoicePosition = TimeSpan.Zero; }
                                _currentlyPlayingMessageId = -1;
                            });
                        };
                    }

                    if (_currentlyPlayingMessageId == msg.Id)
                    {
                        var session = _sharedMediaPlayer.PlaybackSession;
                        if (session.PlaybackState == Windows.Media.Playback.MediaPlaybackState.Playing)
                        {
                            _sharedMediaPlayer.Pause();
                            msg.IsVoicePlaying = false;
                        }
                        else
                        {
                            _sharedMediaPlayer.Play();
                            msg.IsVoicePlaying = true;
                        }
                    }
                    else
                    {
                        var prev = _messages.FirstOrDefault(m => m.Id == _currentlyPlayingMessageId);
                        if (prev != null) { prev.IsVoicePlaying = false; prev.VoiceProgressValue = 0; }

                        var bytes = Convert.FromBase64String(msg.VoiceData);
                        var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                        await stream.WriteAsync(bytes.AsBuffer());
                        stream.Seek(0);

                        _sharedMediaPlayer.Source = Windows.Media.Core.MediaSource.CreateFromStream(stream, "audio/mp3");
                        _sharedMediaPlayer.Volume = msg.VoiceVolumeValue / 100.0; 
                        _sharedMediaPlayer.Play();

                        _currentlyPlayingMessageId = msg.Id;
                        msg.IsVoicePlaying = true;
                    }
                }
                catch { }
            }
        }

        private bool _isDraggingVoiceProgress = false;
        private void VoiceProgressSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isTimerUpdating) return;

            if (sender is Slider slider && slider.Tag is MessageViewModel msg)
            {
                if (_sharedMediaPlayer != null && _currentlyPlayingMessageId == msg.Id)
                {
                    if (msg.VoiceDuration.TotalSeconds > 0)
                    {
                        var newPos = TimeSpan.FromSeconds((e.NewValue / 100.0) * msg.VoiceDuration.TotalSeconds);

                        _sharedMediaPlayer.PlaybackSession.Position = newPos;

                        msg.VoicePosition = newPos;
                    }
                }
            }
        }
        
        private void VoiceVolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            try
            {
                if (sender is Slider slider && slider.Tag is MessageViewModel msg)
                {
                    msg.VoiceVolumeValue = e.NewValue;

                    if (_sharedMediaPlayer != null && _currentlyPlayingMessageId == msg.Id)
                    {
                        _sharedMediaPlayer.Volume = e.NewValue / 100.0;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Volume Error: {ex.Message}");
            }
        }
        private async void MessageImage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is MessageViewModel msg)
            {
                if (string.IsNullOrEmpty(msg.ImageData)) return;
                if (ImageFileNameText != null)
                    ImageFileNameText.Text = msg.FileName ?? "Image";
                var bitmap = await Base64ToBitmap(msg.ImageData);
                if (bitmap == null) return;
                FullSizeImage.Source = bitmap;
                ImagePreviewOverlay.Tag = msg;
                ImagePreviewOverlay.Visibility = Visibility.Visible;
                _dispatcherQueue.TryEnqueue(() =>
                {
                    FitImageToScreen(bitmap.PixelWidth, bitmap.PixelHeight);
                });
            }
        }

        private void FitImageToScreen(int imageWidth, int imageHeight)
        {
            if (ImagePreviewScrollViewer == null || imageWidth == 0 || imageHeight == 0) return;
            double viewportWidth = ImagePreviewScrollViewer.ActualWidth;
            double viewportHeight = ImagePreviewScrollViewer.ActualHeight;
            if (viewportWidth == 0 || viewportHeight == 0) return;
            double scaleX = viewportWidth / imageWidth;
            double scaleY = viewportHeight / imageHeight;
            double zoomFactor = Math.Min(scaleX, scaleY) * 0.95;
            if (zoomFactor > 1.0) zoomFactor = 1.0;
            ImagePreviewScrollViewer.ChangeView(null, null, (float)zoomFactor);
        }

        private void CloseImagePreview_Click(object sender, RoutedEventArgs e)
        {
            ImagePreviewOverlay.Visibility = Visibility.Collapsed;
            FullSizeImage.Source = null; 
            ImagePreviewScrollViewer.ChangeView(null, null, 1.0f); 
        }

        private void ImageOverlay_Tapped(object sender, TappedRoutedEventArgs e)
        {
            CloseImagePreview_Click(null, null); 
        }

        private async void SaveFullImage_Click(object sender, RoutedEventArgs e)
        {
            if (ImagePreviewOverlay.Tag is MessageViewModel msg)
            {
                await SaveFileToDisk(msg.FileName ?? "image.png", msg.ImageData, true);
            }
        }

        private async Task<bool> TryAutoSaveFileAsync(string fileName, string base64Data)
        {
            try
            {
                var settings = uchat.Services.SettingsService.GetForUser(_currentUsername);
                if (!settings.IsAutoDownloadEnabled) return false;
                StorageFolder? folder = null;
                if (!string.IsNullOrEmpty(settings.AutoDownloadToken) &&
                    Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.ContainsItem(settings.AutoDownloadToken))
                {
                    try
                    {
                        folder = await Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.GetFolderAsync(settings.AutoDownloadToken);
                    }
                    catch { folder = null; }
                }
                if (folder == null && !string.IsNullOrEmpty(settings.AutoDownloadPath))
                {
                    try
                    {
                        folder = await StorageFolder.GetFolderFromPathAsync(settings.AutoDownloadPath);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Path access error: {ex.Message}");
                    }
                }
                if (folder != null)
                {
                    var file = await folder.CreateFileAsync(fileName, CreationCollisionOption.GenerateUniqueName);
                    var bytes = Convert.FromBase64String(base64Data);
                    await Windows.Storage.FileIO.WriteBytesAsync(file, bytes);

                    System.Diagnostics.Debug.WriteLine($"[AutoSave] Saved to: {file.Path}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Auto-save failed: {ex.Message}");
            }
            return false;
        }
        private void SettingsPage_ThemeChanged(object? sender, bool isLight)
        {
            _isLightTheme = isLight;
            SetAppTheme(_isLightTheme, null);
        }

        private async void SettingsPage_LogoutRequested(object? sender, EventArgs e)
        {
            try
            {
                await CancelRecordingIfActive();
                if (_networkClient != null)
                {
                    _networkClient.Dispose();
                    _networkClient = null;
                }
                if (_heartbeatTimer != null)
                {
                    _heartbeatTimer.Dispose();
                    _heartbeatTimer = null;
                }
                SetAppTheme(false, sender);
                _isConnected = false;
                _currentUsername = string.Empty;
                _storedPassword = "";
                _messages.Clear();
                _filteredMessages.Clear();
                _usersList.Clear();
                _allUsersCache.Clear();
                _currentChatTarget = null;
                _lastLoadedChat = null;
                UpdateWindowTitle();
                LoginPanel.Visibility = Visibility.Visible;
                ChatPanel.Visibility = Visibility.Collapsed;
                CancelReply_Click(null, null);
                if (SettingsOverlay != null) SettingsOverlay.Visibility = Visibility.Collapsed;
            }
            catch { }
        }

        private async void SettingsPage_DeleteAccountRequested(object? sender, EventArgs e)
        {
            try
            {
                if (_networkClient == null || !_networkClient.IsConnected)
                {
                    var dialog = new ContentDialog
                    {
                        Title = "Error",
                        Content = "Not connected to server.",
                        PrimaryButtonText = "OK",
                        XamlRoot = this.XamlRoot
                    };
                    await dialog.ShowAsync();
                    return;
                }

                await _networkClient.SendMessageAsync(new ProtocolMessage
                {
                    Type = MessageType.DeleteAccount
                });
            }
            catch { }
        }

        private void SetAppTheme(bool isLight, object sender)
        {
            if (App.m_window?.Content is FrameworkElement rootElement)
            {
                rootElement.RequestedTheme = isLight ? ElementTheme.Light : ElementTheme.Dark;
            }
            this.RequestedTheme = isLight ? ElementTheme.Light : ElementTheme.Dark;

            var appResources = Application.Current.Resources;

            Color GetColor(string key)
            {
                if (appResources.TryGetValue(key, out object resource) && resource is Color color)
                {
                    return color;
                }
                return Colors.Black;
            }

            var darkPrefix = "Dark_";
            var lightPrefix = "Light_";
            var currentPrefix = isLight ? lightPrefix : darkPrefix;

            var keys = new[]
            {
        "BackgroundDarkColor",
        "BackgroundDarkerColor",
        "CardBackgroundColor",
        "SidebarBackgroundColor",
        "TextWhiteColor",
        "TextGrayColor",
        "BorderDarkColor",
        "MessageBubbleOwnColor",
        "MessageBubbleOtherColor",
        "PlaceholderColor",
        "SidebarButtonBackgroundColor",
        "SystemMessageColor"
    };

            foreach (var key in keys)
            {
                var targetColor = GetColor(currentPrefix + key);

                string currentBrushKey;
                if (key == "PlaceholderColor") currentBrushKey = "Current_Placeholder";
                else if (key == "MessageBubbleOwnColor") currentBrushKey = "Current_MessageBubbleOwn";
                else if (key == "MessageBubbleOtherColor") currentBrushKey = "Current_MessageBubbleOther";
                else if (key == "SystemMessageColor") currentBrushKey = "Current_SystemMessage";
                else if (key == "SidebarButtonBackgroundColor") currentBrushKey = "Current_SidebarButtonBackground";
                else currentBrushKey = "Current_" + key.Replace("Color", "");

                if (appResources.TryGetValue(currentBrushKey, out object brushResource) && brushResource is SolidColorBrush brush)
                {
                    brush.Color = targetColor;
                }
            }

            UpdatePageBackground();
            UpdateTitleBarColors();
            if (sender is Button btn)
            {
                btn.Content = isLight ? "\u263D" : "\u263C";
            }
        }

        private async void LoadMoreHistory()
        {
            if (_networkClient == null || !_networkClient.IsConnected) return;

            _isLoadingHistory = true;

            var oldestMessage = _messages.FirstOrDefault();
            if (oldestMessage == null)
            {
                _isLoadingHistory = false;
                return;
            }

            var parameters = new Dictionary<string, string>
    {
        { "beforeId", oldestMessage.Id.ToString() }
    };

            if (_currentChatTarget != null)
            {
                var chatUser = _allUsersCache.FirstOrDefault(u => u.Username == _currentChatTarget);

                if (chatUser != null && chatUser.IsGroup)
                {
                    parameters.Add("groupId", chatUser.GroupId.ToString());
                    await _networkClient.SendMessageAsync(new ProtocolMessage
                    {
                        Type = MessageType.GetGroupHistory,
                        Parameters = parameters
                    });
                }
                else
                {
                    parameters.Add("otherUsername", _currentChatTarget);
                    await _networkClient.SendMessageAsync(new ProtocolMessage
                    {
                        Type = MessageType.GetPrivateHistory,
                        Parameters = parameters
                    });
                }
            }
            else
            {
                // Глобальний чат
                await _networkClient.SendMessageAsync(new ProtocolMessage
                {
                    Type = MessageType.GetHistory,
                    Parameters = parameters
                });
            }
        }

        private void LoadLogo()
        {
            UpdateLogoBasedOnConnectionState();
        }

        private void UpdateLogoBasedOnConnectionState()
        {
            try
            {
                if (LogoImage == null) return;

                BitmapImage bitmap;
                if (_isReconnecting)
                {
                    bitmap = new BitmapImage(new Uri("ms-appx:///Assets/uchat_load.gif"));
                }
                else if (!_isConnected && ChatPanel != null && ChatPanel.Visibility == Visibility.Visible)
                {
                    bitmap = new BitmapImage(new Uri("ms-appx:///Assets/uchat_lost.gif"));
                }
                else
                {
                    bitmap = new BitmapImage(new Uri("ms-appx:///uchat_logo.png"));
                }
                LogoImage.Source = bitmap;
            }
            catch { }
        }

        private void LoadTitleBarLogo()
        {
            try
            {
                if (TitleBarLogo != null && TitleBarLogoImage != null)
                {
                    var bitmap = new BitmapImage(new Uri("ms-appx:///uchat_logo.png"));
                    TitleBarLogoImage.Source = bitmap;
                    if (ChatPanel != null) ChatPanel.Loaded += (s, e) => { TitleBarLogo.Visibility = Visibility.Visible; };
                }
            }
            catch { }
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e) => await AttemptConnection(true);
        private async void RegisterButton_Click(object sender, RoutedEventArgs e) => await AttemptConnection(false);

        private async Task AttemptConnection(bool isLogin)
        {
            if (string.IsNullOrWhiteSpace(UsernameTextBox.Text) || string.IsNullOrWhiteSpace(PasswordBox.Password)) return;

            LoginButton.IsEnabled = false; RegisterButton.IsEnabled = false;
            LoadingRing.IsActive = true; LoadingRing.Visibility = Visibility.Visible;
            StatusTextBlock.Visibility = Visibility.Collapsed;

            try
            {
                if (_networkClient != null)
                {
                    _networkClient.MessageReceived -= NetworkClient_MessageReceived;
                    _networkClient.ConnectionLost -= NetworkClient_ConnectionLost;
                    _networkClient.Dispose();
                    _networkClient = null;
                }

                _networkClient = new NetworkClient();
                _networkClient.MessageReceived += NetworkClient_MessageReceived;
                _networkClient.ConnectionLost += NetworkClient_ConnectionLost;

                var connected = await _networkClient.ConnectAsync(_serverIp, _serverPort);
                _isConnected = connected;
                UpdateWindowTitle();
                if (!connected)
                {
                    ShowStatus("Server not found", isError: true);
                    ResetLoginUI();
                    return;
                }
                _storedPassword = PasswordBox.Password;

                await Task.Delay(100);
                await _networkClient.SendMessageAsync(new ProtocolMessage
                {
                    Type = isLogin ? MessageType.Login : MessageType.Register,
                    Parameters = new Dictionary<string, string> { { "username", UsernameTextBox.Text.Trim() }, { "password", PasswordBox.Password } }
                });
            }
            catch (Exception ex)
            {
                ShowStatus($"Error: {ex.Message}", isError: true);
                ResetLoginUI();
            }
        }

        private void ResetLoginUI()
        {
            LoginButton.IsEnabled = true; RegisterButton.IsEnabled = true;
            LoadingRing.IsActive = false; LoadingRing.Visibility = Visibility.Collapsed;
        }

        private void ShowStatus(string message, bool isError)
        {
            StatusTextBlock.Text = message;
            StatusTextBlock.Visibility = Visibility.Visible;
        }

        private void    NetworkClient_MessageReceived(object? sender, ProtocolMessage message)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    switch (message.Type)
                    {
                        case MessageType.LoginResponse:
                        case MessageType.RegisterResponse:
                            HandleAuthResponse(message);
                            break;
                        case MessageType.Error:
                            if (LoginPanel.Visibility == Visibility.Visible)
                            {
                                ShowStatus(message.Data ?? "An error occurred", isError: true);
                                ResetLoginUI();
                            }
                            break;
                        case MessageType.MessageReceived:
                            HandleMessageReceived(message);
                            break;
                        case MessageType.PrivateMessageReceived:
                            HandlePrivateMessageReceived(message);
                            break;
                        case MessageType.HistoryResponse:
                        case MessageType.PrivateHistoryResponse:
                            HandleHistoryResponse(message);
                            break;
                        case MessageType.UsersList:
                            HandleUsersList(message);
                            break;
                        case MessageType.ProfileUpdated:
                            HandleProfileUpdated(message);
                            break;
                        case MessageType.GifListResponse:
                            HandleGifListResponse(message);
                            break;
                        case MessageType.GifReceived:
                            HandleGifReceived(message);
                            break;
                        case MessageType.GifDataResponse:
                            HandleGifDataResponse(message);
                            break;
                        case MessageType.NewUserRegistered:
                            HandleNewUserRegistered(message);
                            break;
                        case MessageType.ScheduledMessagesList:
                            HandleScheduledMessagesList(message);
                            break;
                        case MessageType.DeleteAccountResponse:
                            HandleDeleteAccountResponse(message);
                            break;
                        case MessageType.VoiceMessageReceived:
                            if (_currentChatTarget == null) HandleMessageReceived(message);
                            else HandlePrivateMessageReceived(message);
                            break;
                        case MessageType.ImageReceived:
                            HandleImageReceived(message);
                            break;
                        case MessageType.FileReceived:
                            HandleFileReceived(message);
                            break;
                        case MessageType.FileContentResponse:
                            HandleFileContentResponse(message);
                            break;
                        case MessageType.GroupCreated:
                            HandleGroupCreated(message);
                            break;
                        case MessageType.GroupMessageReceived:
                            HandleGroupMessageReceived(message);
                            break;
                        case MessageType.GroupsList:
                            HandleGroupsList(message);
                            break;
                        case MessageType.GroupHistoryResponse:
                            HandleGroupHistoryResponse(message);
                            break;
                        case MessageType.GroupDetailsResponse:
                            HandleGroupDetailsResponse(message);
                            break;
                        case MessageType.GroupDeleted:
                            HandleGroupDeleted(message);
                            break;
                        case MessageType.ChangePasswordResponse:
                            _dispatcherQueue.TryEnqueue(async () =>
                            {
                                var success = message.Parameters?.GetValueOrDefault("success") == "true";
                                var content = success ? "Password changed successfully!" : "Failed to change password. Check your old password.";
                                var title = success ? "Success" : "Error";

                                var dialog = new ContentDialog { Title = title, Content = content, PrimaryButtonText = "OK", XamlRoot = this.XamlRoot };
                                await dialog.ShowAsync();

                                if (SettingsFrame.Content is SettingsPage settingsPage)
                                {
                                    if (success) settingsPage.ResetPasswordFields();
                                    else settingsPage.EnablePasswordButton();
                                }
                            });
                            break;
                        case MessageType.GroupProfileUpdated:
                            HandleGroupProfileUpdated(message);
                            break;
                        case MessageType.StickerPacksList:
                            HandleStickerPacksList(message);
                            break;
                        case MessageType.StickerPackContent:      
                            HandleStickerPackContent(message);
                            break;
                        case MessageType.StickerDataResponse:      
                            HandleStickerDataResponse(message);
                            break;
                        case MessageType.SearchHistoryResponse:
                            HandleSearchHistoryResponse(message);
                            break;
                        case MessageType.SearchUsersResponse:
                            HandleSearchUsersResponse(message);
                            break;
                        case MessageType.ChatDeleted:
                            _dispatcherQueue.TryEnqueue(() =>
                            {
                                var targetUser = message.Parameters?.GetValueOrDefault("username");
                                if (targetUser != null)
                                {
                                    var userItem = _usersList.FirstOrDefault(u => u.Username == targetUser);
                                    if (userItem != null) _usersList.Remove(userItem);
                                    var cachedItem = _allUsersCache.FirstOrDefault(u => u.Username == targetUser);
                                    if (cachedItem != null) _allUsersCache.Remove(cachedItem);
                                    if (_currentChatTarget == targetUser)
                                    {
                                        LogoButton_Click(null, null);
                                        if (message.Parameters?.ContainsKey("sender") == true)
                                        {
                                            ShowNotification("System", "Chat was deleted by partner", false);
                                        }
                                    }
                                }
                            });
                            break;
                        case MessageType.MessagesRead:
                            _dispatcherQueue.TryEnqueue(() =>
                            {
                                string reader = message.Parameters?.GetValueOrDefault("reader");
                                bool isPrivate = message.Parameters?.ContainsKey("isPrivate") == true;
                                bool isMyReadReceipt = reader == _currentUsername;
                                if (message.Parameters?.ContainsKey("isGlobal") == true)
                                {
                                    bool isGlobalOpen = _currentChatTarget == null || _currentChatTarget == "Global Chat";
                                    if (isGlobalOpen && !isMyReadReceipt)
                                    {
                                        foreach (var msg in _messages.Where(m => m.IsOwnMessage && m.Status == MessageStatusEnum.Sent))
                                        {
                                            msg.Status = MessageStatusEnum.Read;
                                        }
                                    }
                                }
                                if (!string.IsNullOrEmpty(reader) && isPrivate && reader == _currentChatTarget)
                                {
                                    var mySentMessages = _messages
                                        .Where(m => m.IsOwnMessage && m.Status == MessageStatusEnum.Sent)
                                        .ToList();
                                    foreach (var msg in mySentMessages)
                                    {
                                        msg.Status = MessageStatusEnum.Read;
                                    }
                                }
                                else if (message.Parameters?.ContainsKey("groupId") == true && int.TryParse(message.Parameters["groupId"], out int groupId))
                                {
                                    var group = _allUsersCache.FirstOrDefault(u => u.IsGroup && u.GroupId == groupId);
                                    if (group != null)
                                    {
                                        var mySentMessages = _messages.Where(m => m.IsOwnMessage && m.Status == MessageStatusEnum.Sent && m.ChatTarget == group.Username).ToList();
                                        if (_currentChatTarget == group.Username && !isMyReadReceipt)
                                        {
                                            foreach (var msg in _messages.Where(m => m.IsOwnMessage && m.Status == MessageStatusEnum.Sent))
                                            {
                                                msg.Status = MessageStatusEnum.Read;
                                            }
                                        }
                                    }
                                }
                            });
                            break;
                    }
                }
                catch { }
            });
        }

        
        private void RefreshMessagesDisplayNames()
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                foreach (var msg in _messages)
                {
                    if (msg.IsSystemMessage) continue;

                    string newDisplayName = msg.SenderLogin;

                    var user = _allUsersCache.FirstOrDefault(u => u.Username == msg.SenderLogin);

                    if (user != null)
                    {
                        newDisplayName = user.DisplayName;
                    }
                    else if (msg.SenderLogin == _currentUsername)
                    {
                        newDisplayName = _currentDisplayName;
                    }

                    if (msg.SenderUsername != newDisplayName)
                    {
                        msg.SenderUsername = newDisplayName;
                    }
                }
            });
        }

        private string GetSizeStringFromContent(string content)
        {
            try
            {
                long sizeInBytes = 0;
                if (content.StartsWith("<FILE_REF:"))
                {
                    var inner = content.Substring(10, content.Length - 11);
                    var parts = inner.Split('|');
                    if (parts.Length >= 3 && long.TryParse(parts[2], out long bytes))
                    {
                        sizeInBytes = bytes;
                    }
                }
                else if (content.StartsWith("<FILE:"))
                {
                    var inner = content.Substring(6, content.Length - 7);
                    var parts = inner.Split('|', 2);
                    if (parts.Length == 2)
                    {
                        sizeInBytes = (long)(parts[1].Length * 3.0 / 4.0);
                    }
                }
                if (sizeInBytes > 0)
                {
                    return FormatFileSize(sizeInBytes);
                }
            }
            catch { }

            return "File";
        }

        private void HandleFileReceived(ProtocolMessage message)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                if (HandleEditDeleteAction(message)) return;
                var sender = message.Parameters?.GetValueOrDefault("sender") ?? "Unknown";
                var isOwn = sender == _currentUsername;
                var targetUsername = message.Parameters?.GetValueOrDefault("targetUsername");
                bool isPrivate = !string.IsNullOrEmpty(targetUsername);
                DateTime.TryParse(message.Parameters?.GetValueOrDefault("timestamp"), out var time);
                int.TryParse(message.Parameters?.GetValueOrDefault("messageId"), out var msgId);
                string serverData = message.Data ?? "";
                string filename = message.Parameters?.GetValueOrDefault("filename") ?? "file.dat";
                string finalContent = serverData.StartsWith("<FILE_REF:") ? serverData : $"<FILE:{filename}|{serverData}>";
                var processedMsg = new ProtocolMessage
                {
                    Type = MessageType.FileReceived,
                    Data = finalContent,
                    Parameters = message.Parameters
                };
                bool shouldAdd = false;
                if (isPrivate) shouldAdd = isOwn ? (_currentChatTarget != null) : (_currentChatTarget == sender);
                else shouldAdd = (_currentChatTarget == null);
                if (shouldAdd && !isOwn)
                {
                    AddMessageToView(processedMsg, isOwn, isPrivate);

                    if (isPrivate && _currentChatTarget == sender) SendReadReceipt(sender);
                    else if (!isPrivate && _currentChatTarget == null) SendReadReceipt("Global Chat");
                }
                string rawContentForPreview = $"<FILE:{filename}>"; 
                string previewText = FormatPreviewText(sender, isOwn, rawContentForPreview, isPrivate);
                string updateTarget = isPrivate ? (isOwn ? targetUsername : sender) : "Global Chat";
                bool isChatOpen = false;
                if (isPrivate)
                {
                    isChatOpen = _currentChatTarget == updateTarget;
                }
                else
                {
                    isChatOpen = _currentChatTarget == null || _currentChatTarget == "Global Chat";
                }

                bool shouldIncrement = !isOwn && !isChatOpen;
                if (updateTarget != null)
                {
                    UpdateLastMessage(updateTarget, previewText, time != default ? time : (DateTime?)null, true, shouldIncrement);
                }
                if (!isOwn)
                {
                    string title = isPrivate ? sender : "Global Chat";
                    ShowNotification(title, previewText, isPrivate);
                }
                if (isOwn)
                {
                    if (_messages.Any(m => m.Id == msgId)) return;
                    var existingTemp = _messages.LastOrDefault(m => m.IsOwnMessage && m.Id < 0 && m.IsFile);
                    if (existingTemp != null)
                    {
                        existingTemp.Id = msgId;
                        existingTemp.Content = finalContent;
                        if (existingTemp.Status != MessageStatusEnum.Read) existingTemp.Status = MessageStatusEnum.Sent;
                        string sizeStr = GetSizeStringFromContent(finalContent);
                        existingTemp.FileSizeDisplay = $"{sizeStr} • Uploaded";
                    }
                    else
                    {
                        string sizeStr = GetSizeStringFromContent(finalContent);
                        var vm = new MessageViewModel
                        {
                            Id = msgId,
                            SenderUsername = _currentDisplayName,
                            SenderLogin = _currentUsername,
                            SentAt = time,
                            IsOwnMessage = true,
                            Status = MessageStatusEnum.Sent,
                            ChatTarget = isPrivate ? _currentChatTarget : null,
                            Content = finalContent,
                            FileSizeDisplay = $"{sizeStr} • Uploaded"
                        };
                        if (finalContent.StartsWith("<FILE_REF:"))
                        {
                            try
                            {
                                var parts = finalContent.Substring(10, finalContent.Length - 11).Split('|');
                                if (parts.Length > 0 && int.TryParse(parts[0], out int bid)) vm.BlobId = bid;
                            }
                            catch { }
                        }

                        _messages.Add(vm);
                        if (string.IsNullOrEmpty(_searchQuery)) _filteredMessages.Add(vm);
                        ScrollToBottom();
                    }
                }
            });
        }

        private void HandleImageReceived(ProtocolMessage message)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                if (HandleEditDeleteAction(message)) return;
                var sender = message.Parameters?.GetValueOrDefault("sender") ?? "Unknown";
                var isOwn = sender == _currentUsername;
                var targetUsername = message.Parameters?.GetValueOrDefault("targetUsername");
                bool isPrivate = !string.IsNullOrEmpty(targetUsername);
                DateTime.TryParse(message.Parameters?.GetValueOrDefault("timestamp"), out var time);
                int.TryParse(message.Parameters?.GetValueOrDefault("messageId"), out var msgId);
                string serverData = message.Data ?? "";
                string finalContent = serverData.StartsWith("<IMG_REF:") ? serverData : $"<IMAGE:{serverData}>";
                var processedMsg = new ProtocolMessage
                {
                    Type = MessageType.ImageReceived,
                    Data = finalContent,
                    Parameters = message.Parameters
                };
                bool shouldAdd = false;
                if (isPrivate) shouldAdd = isOwn ? (_currentChatTarget != null) : (_currentChatTarget == sender);
                else shouldAdd = (_currentChatTarget == null);
                if (shouldAdd && !isOwn)
                {
                    AddMessageToView(processedMsg, isOwn, isPrivate);
                    if (isPrivate && _currentChatTarget == sender) SendReadReceipt(sender);
                    else if (!isPrivate && _currentChatTarget == null) SendReadReceipt("Global Chat");
                }
                string rawContentForPreview = "<IMAGE:>";
                string previewText = FormatPreviewText(sender, isOwn, rawContentForPreview, isPrivate);
                string updateTarget = isPrivate ? (isOwn ? targetUsername : sender) : "Global Chat";
                bool isChatOpen = false;
                if (isPrivate)
                {
                    isChatOpen = _currentChatTarget == updateTarget;
                }
                else
                {
                    isChatOpen = _currentChatTarget == null || _currentChatTarget == "Global Chat";
                }

                bool shouldIncrement = !isOwn && !isChatOpen;
                if (updateTarget != null)
                {
                    UpdateLastMessage(updateTarget, previewText, time != default ? time : (DateTime?)null, true, shouldIncrement);
                }

                if (!isOwn)
                {
                    string title = isPrivate ? sender : "Global Chat";
                    ShowNotification(title, previewText, isPrivate);
                }
                if (isOwn)
                {
                    if (_messages.Any(m => m.Id == msgId)) return;
                    var existing = _messages.LastOrDefault(m => m.IsOwnMessage && m.Id < 0 && m.IsImage);
                    if (existing != null)
                    {
                        existing.Id = msgId;
                        existing.SentAt = time;
                        if (existing.Status != MessageStatusEnum.Read) existing.Status = MessageStatusEnum.Sent;
                    }
                    else
                    {
                        var vm = new MessageViewModel
                        {
                            Id = msgId,
                            SenderUsername = _currentDisplayName,
                            SenderLogin = _currentUsername,
                            SentAt = time,
                            IsOwnMessage = true,
                            Status = MessageStatusEnum.Sent,
                            ChatTarget = isPrivate ? _currentChatTarget : null,
                            Content = finalContent,
                            IsImage = true
                        };
                        if (finalContent.StartsWith("<IMG_REF:"))
                        {
                            try
                            {
                                var parts = finalContent.Substring(9, finalContent.Length - 10).Split('|');
                                if (parts.Length > 0 && int.TryParse(parts[0], out int bid)) vm.BlobId = bid;
                            }
                            catch { }
                        }

                        _messages.Add(vm);
                        if (string.IsNullOrEmpty(_searchQuery)) _filteredMessages.Add(vm);
                        ScrollToBottom();
                    }
                }
            });
        }

        private async void HandleFileContentResponse(ProtocolMessage message)
        {
            _dispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    if (message.Parameters != null &&
                        message.Parameters.ContainsKey("blobId") &&
                        int.TryParse(message.Parameters["blobId"], out int blobId))
                    {
                        var targetMessages = _messages.Where(m => m.BlobId == blobId).ToList();
                        if (targetMessages.Any())
                        {
                            string data = message.Data;
                            if (string.IsNullOrEmpty(data)) return;
                            var firstMsg = targetMessages.First();
                            var ext = System.IO.Path.GetExtension(firstMsg.FileName ?? "").ToLower();
                            bool isImage = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".jfif", ".webp" }.Contains(ext);
                            Microsoft.UI.Xaml.Media.Imaging.BitmapImage? sharedBitmap = null;
                            if (isImage)
                            {
                                sharedBitmap = await Base64ToBitmap(data);
                            }
                            foreach (var targetMsg in targetMessages)
                            {
                                if (isImage)
                                {
                                    targetMsg.ImageData = data;
                                    targetMsg.DisplayImage = sharedBitmap;
                                    targetMsg.IsImage = true;
                                    targetMsg.IsFile = false;
                                }
                                else
                                {
                                    targetMsg.FileData = data;
                                    long actualBytes = (long)(data.Length * 3.0 / 4.0);
                                    string size = FormatFileSize(actualBytes);
                                    targetMsg.FileSizeDisplay = $"{size} • Downloaded";
                                }
                                if (targetMsg.IsUserDownloading)
                                {
                                    targetMsg.IsUserDownloading = false;
                                    bool autoSaved = await TryAutoSaveFileAsync(targetMsg.FileName, data);
                                    if (!autoSaved)
                                    {
                                        await SaveFileToDisk(targetMsg.FileName ?? "file", data, isImage);
                                    }
                                    else
                                    {
                                        targetMsg.FileSizeDisplay = "Saved to Downloads";
                                    }
                                }
                                else if (isImage)
                                {
                                    await TryAutoSaveFileAsync(firstMsg.FileName, data);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Error processing file content: " + ex.Message);
                }
            });
        }

        private async void HandleDeleteAccountResponse(ProtocolMessage message)
        {
            var success = message.Parameters?.GetValueOrDefault("success") == "true";

            if (success)
            {
                if (_networkClient != null) { _networkClient.Dispose(); _networkClient = null; }
                if (_heartbeatTimer != null) { _heartbeatTimer.Dispose(); _heartbeatTimer = null; }
                if (!string.IsNullOrEmpty(_currentUsername)) { uchat.Services.SettingsService.DeleteUser(_currentUsername); }
                _isConnected = false;
                _currentUsername = string.Empty;
                _storedPassword = "";
                _messages.Clear();
                _filteredMessages.Clear();
                _usersList.Clear();
                _allUsersCache.Clear();
                _currentChatTarget = null;
                SetAppTheme(false, null);
                UpdateWindowTitle();
                LoginPanel.Visibility = Visibility.Visible;
                ChatPanel.Visibility = Visibility.Collapsed;

                if (SettingsOverlay != null) SettingsOverlay.Visibility = Visibility.Collapsed;

                var dialog = new ContentDialog
                {
                    Title = "Account Deleted",
                    Content = "Your account has been successfully deleted.",
                    PrimaryButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
            }
            else
            {
                var dialog = new ContentDialog
                {
                    Title = "Error",
                    Content = message.Data ?? "Failed to delete account.",
                    PrimaryButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
            }
        }

        private async void HandleAuthResponse(ProtocolMessage message)
        {
            var success = message.Parameters?.GetValueOrDefault("success") == "true";
            if (success)
            {
                _currentUsername = message.Parameters.GetValueOrDefault("username") ?? "";
                _currentDisplayName = message.Parameters.GetValueOrDefault("displayName") ?? _currentUsername;
                _currentBio = message.Parameters.GetValueOrDefault("bio") ?? "";
                _currentColor = message.Parameters.GetValueOrDefault("color") ?? "#0088CC";
                _currentAvatarBase64 = message.Parameters.GetValueOrDefault("avatar") ?? "";

                if (int.TryParse(message.Parameters.GetValueOrDefault("userId"), out int uid))
                {
                    _currentUserId = uid;
                }


                var settings = uchat.Services.SettingsService.GetForUser(_currentUsername);
                _isLightTheme = settings.IsLightTheme;
                SetAppTheme(_isLightTheme, null);
                await UpdateMyProfileUI();
                _dispatcherQueue.TryEnqueue(() =>
                {
                    if (ConnectionStatusTextBlock != null)
                    {
                        ConnectionStatusTextBlock.Text = "Online";
                        ConnectionStatusTextBlock.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 76, 175, 80));
                    }
                    _isConnected = true;
                    if (_isReconnecting)
                    {
                        _isReconnecting = false;
                    }
                    UpdateWindowTitle();
                    UpdateLogoBasedOnConnectionState();

                    LoadingRing.IsActive = false;
                    LoadingRing.Visibility = Visibility.Collapsed;
                    LoginButton.IsEnabled = true;
                    RegisterButton.IsEnabled = true;
                    LoginPanel.Visibility = Visibility.Collapsed;
                    ChatPanel.Visibility = Visibility.Visible;
                    HookChatScroll();
                    if (TitleBarLogo != null) TitleBarLogo.Visibility = Visibility.Visible;
                });

                InitializeUserList();
                _ = _networkClient?.SendMessageAsync(new ProtocolMessage { Type = MessageType.GetUsers });
                _ = _networkClient?.SendMessageAsync(new ProtocolMessage { Type = MessageType.GetGroups });
                _dispatcherQueue.TryEnqueue(async () =>
                {
                    await Task.Delay(200);
                    LogoButton_Click(null, null);
                });

                StartHeartbeat();
                _isScrolledToBottom = true;
                _hasNewMessages = false;
                UpdateGoToNewButtonVisibility();
            }
            else
            {
                _isConnected = false;
                UpdateWindowTitle();
                ShowStatus(message.Data ?? "Auth failed", isError: true);
                ResetLoginUI();
                _storedPassword = "";
            }
        }

        private void UpdateWindowTitle()
        {
            try
            {
                if (App.m_window != null)
                {
                    var status = _isConnected ? "connected" : "disconnected";
                    App.m_window.Title = $"CHAT | {_currentUsername} [{status}]";
                }
            }
            catch { }
        }

        private async void HandleProfileUpdated(ProtocolMessage message)
        {
            if (message.Parameters?.ContainsKey("isGlobalUpdate") == true && message.Parameters.ContainsKey("userId"))
            {
                int userId = int.Parse(message.Parameters["userId"]);
                string remoteName = message.Parameters["username"];
                string remoteRealUsername = message.Parameters.GetValueOrDefault("realUsername");
                string remoteBio = message.Parameters.GetValueOrDefault("bio"); 
                string remoteAvatar = message.Data;
                bool isDeleted = message.Parameters.ContainsKey("isDeleted") && message.Parameters["isDeleted"] == "true";
                _dispatcherQueue.TryEnqueue(async () =>
                {
                    var userInCache = _allUsersCache.FirstOrDefault(u => u.Id == userId && !u.IsGroup);
                    var userInList = _usersList.FirstOrDefault(u => u.Id == userId && !u.IsGroup);
                    Func<UserItemModel, Task> updateUserModel = async (model) =>
                    {
                        model.DisplayName = remoteName;
                        if (remoteBio != null) model.Bio = remoteBio;
                        model.IsDeleted = isDeleted;
                        if (isDeleted) model.AvatarColor = "#808080";
                        if (!string.IsNullOrEmpty(remoteAvatar))
                        {
                            model.AvatarData = remoteAvatar; 
                            model.ProfileImage = await Base64ToBitmap(remoteAvatar);
                        }
                        else if (remoteAvatar == "") 
                        {
                            model.AvatarData = null;
                            model.ProfileImage = null;
                        }
                        if (!string.IsNullOrEmpty(remoteRealUsername))
                        {
                            model.Username = remoteRealUsername;
                        }
                    };
                    if (userInCache != null)
                    {
                        if (_currentChatTarget == userInCache.Username ||
                           (!string.IsNullOrEmpty(remoteRealUsername) && _currentChatTarget == remoteRealUsername))
                        {
                            if (!string.IsNullOrEmpty(remoteRealUsername)) _currentChatTarget = remoteRealUsername;
                            CurrentChatTitle.Text = remoteName;
                        }
                        await updateUserModel(userInCache);
                    }
                    if (userInList != null)
                    {
                        if (userInList != userInCache) await updateUserModel(userInList);
                        int index = _usersList.IndexOf(userInList);
                        if (index >= 0)
                        {
                            _usersList.RemoveAt(index);
                            _usersList.Insert(index, userInList);
                            if (_currentChatTarget == userInList.Username)
                            {
                                UsersListView.SelectedItem = userInList;
                            }
                        }
                    }
                    RefreshUserListUI();
                });
                return;
            }
            var newDisplayName = message.Parameters?.GetValueOrDefault("username");
            var newBio = message.Parameters?.GetValueOrDefault("bio");
            var newColor = message.Parameters?.GetValueOrDefault("color");
            if (newBio != null) _currentBio = newBio;
            if (newColor != null) _currentColor = newColor;
            if (!string.IsNullOrEmpty(newDisplayName)) _currentDisplayName = newDisplayName;
            await UpdateMyProfileUI();
        }

        private void ChatItemBorder_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
        {
            if (sender is FrameworkElement fe && fe.DataContext is Models.UserItemModel user)
            {
                if (user.Username == "Global Chat")
                {
                    args.Handled = true;
                }
            }
        }

        private void ChatMenu_Opening(object sender, object e)
        {
            if (sender is MenuFlyout flyout)
            {
                if (flyout.Target is FrameworkElement targetElement &&
                    targetElement.DataContext is uchat.Models.UserItemModel user)
                {
                    if (user.Username == "Global Chat")
                    {
                        flyout.Hide();
                    }
                }
            }
        }

        private async Task UpdateGroupInList(int groupId, string newName, string? newAvatarBase64)
        {
            try
            {
                var groupInList = _usersList.FirstOrDefault(u => u.IsGroup && u.GroupId == groupId);
                Microsoft.UI.Xaml.Media.ImageSource? newImageSource = null;
                string? newAvatarData = groupInList?.AvatarData;
                bool isDeleting = (newAvatarBase64 == "DELETE") || (newAvatarBase64 == "");

                if (isDeleting)
                {
                    newAvatarData = null;
                    newImageSource = null;
                }
                else if (!string.IsNullOrEmpty(newAvatarBase64))
                {
                    newAvatarData = newAvatarBase64;
                    newImageSource = await Base64ToBitmap(newAvatarBase64);
                }
                else if (groupInList != null)
                {
                    newImageSource = groupInList.ProfileImage;
                }
                Action<UserItemModel> updateModel = (model) =>
                {
                    model.DisplayName = newName;
                    model.AvatarData = newAvatarData;
                    model.ProfileImage = newImageSource;
                    if (model.Username != newName)
                    {
                        model.Username = newName;
                    }
                    else if (isDeleting)
                    {
                        string temp = newName;
                        model.Username = temp + " ";
                        model.Username = temp;
                    }
                };
                var groupInCache = _allUsersCache.FirstOrDefault(u => u.IsGroup && u.GroupId == groupId);
                if (groupInCache != null) updateModel(groupInCache);
                if (_currentChatTarget == groupInCache?.Username ||
                   (CurrentChatTitle.Text == groupInCache?.Username && groupInCache != null))
                {
                    _currentChatTarget = newName;
                    if (CurrentChatTitle != null) CurrentChatTitle.Text = newName;
                }
                if (GroupProfileOverlay.Visibility == Visibility.Visible && GroupProfileOverlay.Tag is int gid && gid == groupId)
                {
                    if (GroupNameBox != null) GroupNameBox.Text = newName;

                    if (isDeleting)
                    {
                        GroupAvatarImage.Source = null;
                        GroupAvatarImage.Visibility = Visibility.Collapsed;
                        GroupInitials.Visibility = Visibility.Visible;
                        GroupInitials.Text = !string.IsNullOrEmpty(newName) ? newName.Substring(0, 1).ToUpper() : "?";
                    }
                    else if (newImageSource != null)
                    {
                        GroupAvatarImage.Source = newImageSource;
                        GroupAvatarImage.Visibility = Visibility.Visible;
                        GroupInitials.Visibility = Visibility.Collapsed;
                    }
                }
                if (groupInList != null)
                {
                    updateModel(groupInList);
                    int index = _usersList.IndexOf(groupInList);
                    if (index >= 0)
                    {
                        _usersList.RemoveAt(index);
                        _usersList.Insert(index, groupInList);

                        if (_currentChatTarget == newName) UsersListView.SelectedItem = groupInList;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Update Group UI Error: {ex.Message}");
            }
        }
        private void HandleGroupProfileUpdated(ProtocolMessage message)
        {
            if (message.Parameters == null) return;
            if (int.TryParse(message.Parameters.GetValueOrDefault("groupId"), out int groupId))
            {
                string newName = message.Parameters.GetValueOrDefault("name") ?? "Group";
                string? serverData = message.Data;
                string avatarCommand = string.IsNullOrEmpty(serverData) ? "DELETE" : serverData;
                _ = UpdateGroupInList(groupId, newName, avatarCommand);
            }
        }
        
        private async Task UpdateMyProfileUI(string? displayNameOverride = null)
        {
            string displayToShow = displayNameOverride ?? _currentDisplayName;
            if (string.IsNullOrEmpty(displayToShow)) displayToShow = _currentUsername;

            if (MyUsernameText != null) MyUsernameText.Text = displayToShow;

            if (MyAvatarInitials != null && displayToShow.Length > 0)
                MyAvatarInitials.Text = displayToShow.Substring(0, 1).ToUpper();

            if (MyProfileEllipse != null)
            {
                try { MyProfileEllipse.Fill = new SolidColorBrush(ParseColor(_currentColor)); } catch { }
            }

            if (MyProfileImage != null)
            {
                if (!string.IsNullOrEmpty(_currentAvatarBase64))
                    MyProfileImage.Source = await Base64ToBitmap(_currentAvatarBase64);
                else
                    MyProfileImage.Source = null;
            }
        }

        private Windows.UI.Color ParseColor(string hex)
        {
            try
            {
                hex = hex.Replace("#", "");
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                return Windows.UI.Color.FromArgb(255, r, g, b);
            }
            catch { return Windows.UI.Color.FromArgb(255, 0, 136, 204); }
        }

        private void StartHeartbeat()
        {
            _heartbeatTimer?.Dispose();
            _heartbeatTimer = new System.Threading.Timer(async _ =>
            {
                try
                {
                    if (_networkClient?.IsConnected == true)
                    {
                        var sent = await _networkClient.SendMessageAsync(new ProtocolMessage { Type = MessageType.Heartbeat });
                        if (!sent)
                        {
                            Console.WriteLine("Heartbeat failed - connection may be lost");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Heartbeat error: {ex.Message}");
                }
            }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }
        private void CloseOverlays_Tapped(object sender, TappedRoutedEventArgs e)
        {
            HideAllOverlays();
        }

        private void CloseOverlays_Click(object sender, RoutedEventArgs e)
        {
            HideAllOverlays();
        }


        private void HideAllOverlays()
        {
            if (EditProfileOverlay != null) EditProfileOverlay.Visibility = Visibility.Collapsed;
            if (SettingsOverlay != null) SettingsOverlay.Visibility = Visibility.Collapsed;
            if (UserProfileOverlay != null) UserProfileOverlay.Visibility = Visibility.Collapsed;
            if (GroupProfileOverlay != null) GroupProfileOverlay.Visibility = Visibility.Collapsed;
        }
        private async void ProfileHeader_Tapped(object sender, TappedRoutedEventArgs e)
        {
            EditUsernameBox.Text = _currentDisplayName;
            EditBioBox.Text = _currentBio;

            if (EditLoginText != null)
            {
                EditLoginText.Text = $"@{_currentUsername}";
            }

            if (string.IsNullOrEmpty(_currentAvatarBase64))
            {
                EditAvatarImage.Source = null;
                EditAvatarImage.Visibility = Visibility.Collapsed;
                EditAvatarPreview.Fill = new SolidColorBrush(ParseColor(_currentColor));
            }
            else
            {
                await ShowPreview();
                EditAvatarImage.Visibility = Visibility.Visible;
            }

            if (EditProfileOverlay != null)
            {
                EditProfileOverlay.Visibility = Visibility.Visible;
            }
        }

        private async void CreateGroup_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CreateGroupDialog(_allUsersCache);
            dialog.XamlRoot = this.XamlRoot;
            dialog.RequestedTheme = this.ActualTheme;
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                var memberIds = dialog.SelectedUserIds;
                var groupName = dialog.GroupName.Trim();
                if (groupName.Equals("Global Chat", StringComparison.OrdinalIgnoreCase))
                {
                    await new ContentDialog
                    {
                        Title = "Invalid Name",
                        Content = "This name is reserved.",
                        PrimaryButtonText = "OK",
                        XamlRoot = this.XamlRoot
                    }.ShowAsync();
                    return;
                }
                if (groupName.Equals(_currentUsername, StringComparison.OrdinalIgnoreCase))
                {
                    await new ContentDialog
                    {
                        Title = "Invalid Name",
                        Content = "You cannot name a group with your own login.",
                        PrimaryButtonText = "OK",
                        XamlRoot = this.XamlRoot
                    }.ShowAsync();
                    return;
                }
                var conflictUser = _allUsersCache.FirstOrDefault(u =>!u.IsGroup &&u.Username.Equals(groupName, StringComparison.OrdinalIgnoreCase));
                if (conflictUser != null)
                {
                    await new ContentDialog
                    {
                        Title = "Invalid Name",
                        Content = $"The login '{groupName}' is already taken by a user. Please choose another name.",
                        PrimaryButtonText = "OK",
                        XamlRoot = this.XamlRoot
                    }.ShowAsync();
                    return;
                }
                var conflictGroup = _allUsersCache.FirstOrDefault(u =>u.IsGroup && u.Username.Equals(groupName, StringComparison.OrdinalIgnoreCase));
                if (conflictGroup != null)
                {
                    await new ContentDialog
                    {
                        Title = "Name Taken",
                        Content = $"A group named '{groupName}' already exists in your list.",
                        PrimaryButtonText = "OK",
                        XamlRoot = this.XamlRoot
                    }.ShowAsync();
                    return;
                }
                var jsonIds = System.Text.Json.JsonSerializer.Serialize(memberIds);

                await _networkClient.SendMessageAsync(new ProtocolMessage
                {
                    Type = MessageType.CreateGroup,
                    Data = jsonIds,
                    Parameters = new Dictionary<string, string> { { "name", groupName } }
                });
            }
        }

        private void HandleGroupCreated(ProtocolMessage message)
        {
            var groupId = int.Parse(message.Data);
            var name = message.Parameters["name"];

            var newGroup = new UserItemModel
            {
                Id = groupId,
                Username = name,
                IsGroup = true, 
                GroupId = groupId,
                AvatarColor = "#4CAF50", 
            };

            _allUsersCache.Add(newGroup);
            RefreshUserListUI();
        }

        private void HandleGroupMessageReceived(ProtocolMessage message)
        {
            if (!int.TryParse(message.Parameters?.GetValueOrDefault("groupId"), out int groupId)) return;
            var group = _allUsersCache.FirstOrDefault(u => u.IsGroup && u.GroupId == groupId);

            if (HandleEditDeleteAction(message)) return;

            if (group == null) return;

            var senderName = message.Parameters?.GetValueOrDefault("sender") ?? "Unknown";
            var rawContent = message.Data ?? "";

            DateTime.TryParse(message.Parameters?.GetValueOrDefault("timestamp"), out var time);
            if (time == default) time = DateTime.UtcNow;
            bool isOwn = senderName == _currentUsername;
            string contentPreview = GetContentPreview(rawContent);
            string senderPrefix = isOwn ? "You" : senderName;
            string previewText = $"{senderPrefix}: {contentPreview}";
            var activeChat = _allUsersCache.FirstOrDefault(u => u.Username == _currentChatTarget);
            bool isViewingThisGroup = activeChat != null && activeChat.IsGroup && activeChat.GroupId == groupId;
            bool shouldIncrement = (senderName != _currentUsername) && (!activeChat?.IsGroup == true || activeChat?.GroupId != groupId);
            UpdateLastMessage(group.Username, previewText, time, true, shouldIncrement);
            if (isViewingThisGroup)
            {
                int.TryParse(message.Parameters?.GetValueOrDefault("messageId"), out var msgId);
                isOwn = senderName == _currentUsername;

                int? replyToId = null;
                string? replySender = null;
                string? replyContent = null;
                if (!isOwn)
                {
                    SendReadReceipt(group.Username); 
                }
                if (message.Parameters != null && message.Parameters.ContainsKey("replyToId"))
                {
                    if (int.TryParse(message.Parameters["replyToId"], out int rId))
                    {
                        replyToId = rId;
                        var repliedMsg = _messages.FirstOrDefault(m => m.Id == rId);
                        if (repliedMsg != null)
                        {
                            replySender = repliedMsg.SenderUsername;
                            replyContent = repliedMsg.Content;
                        }
                        else
                        {
                            replySender = "User";
                            replyContent = "Loading...";
                        }
                    }
                }

                _dispatcherQueue.TryEnqueue(() =>
                {
                    if (isOwn)
                    {
                        var existing = _messages.LastOrDefault(m => m.IsOwnMessage && m.Id < 0);
                        if (existing != null)
                        {
                            existing.Id = msgId;
                            existing.Content = rawContent;
                            existing.Status = MessageStatusEnum.Sent;
                            if (existing.IsFile)
                            {
                                string sizeStr = GetSizeStringFromContent(rawContent);
                                existing.FileSizeDisplay = $"{sizeStr} • Uploaded";
                            }
                            return;
                        }
                    }

                    var vm = new MessageViewModel
                    {
                        Id = msgId,
                        SenderUsername = senderName,
                        SentAt = time,
                        IsOwnMessage = isOwn,
                        IsSystemMessage = false,
                        MessageStatus = "sent",
                        ChatTarget = group.Username,
                        Content = rawContent,
                        ReplyToId = replyToId,
                        ReplyToSender = replySender,
                        ReplyToContent = replyContent
                    };

                    if (vm.IsGif && rawContent.StartsWith("<GIF:") && rawContent.EndsWith(">"))
                    {
                        var start = rawContent.IndexOf(":") + 1;
                        var end = rawContent.LastIndexOf(">");
                        if (start > 0 && end > start)
                        {
                            var filename = rawContent.Substring(start, end - start);
                            if (_gifPreviewCache.TryGetValue(filename, out var cached))
                            {
                                vm.GifData = cached;
                                _ = Task.Run(async () =>
                                {
                                    var bmp = await Base64ToBitmap(cached);
                                    _dispatcherQueue.TryEnqueue(() => vm.DisplayImage = bmp);
                                });
                            }
                            else if (_networkClient?.IsConnected == true)
                            {
                                _ = Task.Run(() => _networkClient.SendMessageAsync(new ProtocolMessage { Type = MessageType.GetGif, Data = filename }));
                            }
                        }
                    }
                    CheckAndLoadSticker(vm);
                    _messages.Add(vm);
                    if (string.IsNullOrEmpty(_searchQuery)) _filteredMessages.Add(vm);

                    if (isOwn || _isScrolledToBottom)
                    {
                        _dispatcherQueue.TryEnqueue(ScrollToBottom);
                    }
                    else
                    {
                        _hasNewMessages = true;
                        UpdateGoToNewButtonVisibility();
                    }

                    if (!isOwn && vm.BlobId.HasValue)
                    {
                        var ext = System.IO.Path.GetExtension(vm.FileName ?? "").ToLower();
                        bool isImg = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" }.Contains(ext);
                        var settings = uchat.Services.SettingsService.GetForUser(_currentUsername);
                        bool shouldDownload = false;
                        if (isImg) shouldDownload = vm.DisplayImage == null;
                        else shouldDownload = settings.IsAutoDownloadEnabled;

                        if (shouldDownload)
                        {
                            _ = Task.Run(async () =>
                            {
                                await Task.Delay(50);
                                if (_networkClient?.IsConnected == true)
                                    await _networkClient.SendMessageAsync(new ProtocolMessage
                                    {
                                        Type = MessageType.GetFileContent,
                                        Data = vm.BlobId.Value.ToString()
                                    });
                            });
                        }
                    }
                });
            }
            else
            {
                if (senderName != _currentUsername)
                {
                    ShowNotification(group.Username, previewText, false);
                }
            }
        }

        private void HandleGroupsList(ProtocolMessage message)
        {
            if (string.IsNullOrEmpty(message.Data)) return;

            try
            {
                var groups = System.Text.Json.JsonSerializer.Deserialize<List<UserItemModel>>(message.Data);

                if (groups != null)
                {
                    _dispatcherQueue.TryEnqueue(async () =>
                    {
                        foreach (var g in groups)
                        {
                            g.IsGroup = true;
                            g.IsGroupOwner = (g.CreatorId == _currentUserId);
                            g.RefreshMenuText();
                            if (!string.IsNullOrEmpty(g.AvatarData))
                            {
                                g.ProfileImage = await Base64ToBitmap(g.AvatarData);
                            }
                            else
                            {
                                g.ProfileImage = null;
                            }

                            var existing = _allUsersCache.FirstOrDefault(u => u.IsGroup && u.GroupId == g.GroupId);

                            if (existing == null)
                            {
                                _usersList.Add(g);
                                _allUsersCache.Add(g);
                            }
                            else
                            {
                                existing.Username = g.Username;
                                existing.AvatarData = g.AvatarData;
                                existing.ProfileImage = g.ProfileImage;
                            }
                        }

                        RefreshUserListUI();
                        
                        _ = Task.Run(async () =>
                        {
                            foreach (var group in groups)
                            {
                                await Task.Delay(50);
                                
                                if (_networkClient?.IsConnected == true)
                                {
                                    await _networkClient.SendMessageAsync(new ProtocolMessage
                                    {
                                        Type = MessageType.GetGroupHistory,
                                        Parameters = new Dictionary<string, string> { { "groupId", group.GroupId.ToString() } }
                                    });
                                }
                            }
                        });
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Groups List Error: {ex.Message}");
            }
        }

        private void HandleGroupHistoryResponse(ProtocolMessage message)
        {
            if (string.IsNullOrEmpty(message.Data)) return;
            if (!int.TryParse(message.Parameters?.GetValueOrDefault("groupId"), out int groupId)) return;
            try
            {
                var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var rawHistory = System.Text.Json.JsonSerializer.Deserialize<List<MessageViewModel>>(message.Data, options);
                var activeChat = _allUsersCache.FirstOrDefault(u => u.Username == _currentChatTarget);
                if (activeChat != null && activeChat.IsGroup && activeChat.GroupId == groupId)
                {
                    _dispatcherQueue.TryEnqueue(async () =>
                    {
                        _messages.Clear();
                        _filteredMessages.Clear();

                        if (rawHistory != null)
                        {
                            foreach (var rawMsg in rawHistory)
                            {
                                var content = (rawMsg.Content ?? "").Trim();
                                rawMsg.Content = (rawMsg.Content ?? "").Trim();
                                rawMsg.IsOwnMessage = rawMsg.SenderUsername == _currentUsername;
                                rawMsg.ChatTarget = activeChat.Username;
                                if (content.StartsWith("<GIF:") && content.EndsWith(">"))
                                {
                                    rawMsg.IsGif = true;
                                    var start = content.IndexOf(":") + 1;
                                    var end = content.LastIndexOf(">");
                                    if (start > 0 && end > start)
                                    {
                                        var filename = content.Substring(start, end - start);
                                        if (_gifPreviewCache.TryGetValue(filename, out var cached))
                                        {
                                            rawMsg.GifData = cached;
                                            rawMsg.DisplayImage = await Base64ToBitmap(cached);
                                        }
                                        else if (_networkClient?.IsConnected == true)
                                        {
                                            _ = Task.Run(() => _networkClient.SendMessageAsync(new ProtocolMessage { Type = MessageType.GetGif, Data = filename }));
                                        }
                                    }
                                }
                                if (rawMsg.BlobId.HasValue)
                                {
                                    if (rawMsg.IsImage && rawMsg.DisplayImage == null)
                                    {
                                        _ = Task.Run(async () =>
                                        {
                                            await Task.Delay(50);
                                            if (_networkClient?.IsConnected == true)
                                                await _networkClient.SendMessageAsync(new ProtocolMessage { Type = MessageType.GetFileContent, Data = rawMsg.BlobId.Value.ToString() });
                                        });
                                    }
                                }
                                rawMsg.IsOwnMessage = string.Equals(rawMsg.SenderUsername, _currentUsername, StringComparison.OrdinalIgnoreCase);
                                rawMsg.ChatTarget = activeChat.Username;
                                if (rawMsg.IsOwnMessage)
                                {
                                    rawMsg.Status = MessageStatusEnum.Sent;
                                    if (rawMsg.IsRead || rawMsg.MessageStatus == "read")
                                    {
                                        rawMsg.Status = MessageStatusEnum.Read;
                                    }
                                }
                                CheckAndLoadSticker(rawMsg);
                                _messages.Add(rawMsg);
                                _filteredMessages.Add(rawMsg);
                            }
                        }
                        await Task.Delay(50);
                        ScrollToBottom();
                        _isScrolledToBottom = true;
                    });
                }
                
                if (rawHistory != null && rawHistory.Count > 0)
                {
                    var group = _allUsersCache.FirstOrDefault(u => u.IsGroup && u.GroupId == groupId);
                    if (group != null)
                    {
                        var lastMessage = rawHistory
                            .Where(m => !m.IsSystemMessage && m.SenderUsername != "System")
                            .OrderByDescending(m => m.SentAt)
                            .FirstOrDefault();

                        if (lastMessage != null)
                        {
                            var content = lastMessage.Content ?? "";
                            string formattedPreview = FormatPreviewText(lastMessage.SenderUsername, lastMessage.IsOwnMessage, content, false);
                            UpdateLastMessage(group.Username, formattedPreview, lastMessage.SentAt, true, false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Group Hist Error: {ex.Message}");
            }
        }
        private async void GroupAddMember_Click(object sender, RoutedEventArgs e)
        {
            if (GroupProfileOverlay.Tag is int groupId && !string.IsNullOrWhiteSpace(GroupAddMemberBox.Text))
            {
                await _networkClient.SendMessageAsync(new ProtocolMessage
                {
                    Type = MessageType.AddGroupMember,
                    Data = GroupAddMemberBox.Text,
                    Parameters = new Dictionary<string, string> { { "groupId", groupId.ToString() } }
                });
                GroupAddMemberBox.Text = "";
            }
        }

        private async void KickMember_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int userId && GroupProfileOverlay.Tag is int groupId)
            {
                await _networkClient.SendMessageAsync(new ProtocolMessage
                {
                    Type = MessageType.RemoveGroupMember,
                    Data = userId.ToString(),
                    Parameters = new Dictionary<string, string> { { "groupId", groupId.ToString() } }
                });
            }
        }
        
        private void GroupRemovePhoto_Click(object sender, RoutedEventArgs e)
        {
            _currentGroupAvatarBase64 = "DELETE";

            if (GroupAvatarImage != null)
            {
                GroupAvatarImage.Source = null;
                GroupAvatarImage.Visibility = Visibility.Collapsed;
            }

            if (GroupInitials != null)
            {
                GroupInitials.Visibility = Visibility.Visible;
                string name = GroupNameBox.Text;
                GroupInitials.Text = !string.IsNullOrEmpty(name) ? name.Substring(0, 1).ToUpper() : "?";
            }
        }
        private async void GroupDelete_Click(object sender, RoutedEventArgs e)
        {
            if (GroupProfileOverlay.Tag is int groupId)
            {
                await _networkClient.SendMessageAsync(new ProtocolMessage
                {
                    Type = MessageType.DeleteGroup,
                    Data = groupId.ToString()
                });
                GroupProfileOverlay.Visibility = Visibility.Collapsed;
            }
        }
        private void HandleGroupDetailsResponse(ProtocolMessage message)
        {
            try
            {
                var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var groupDetails = System.Text.Json.JsonSerializer.Deserialize<GroupDetailsModel>(message.Data, options);

                if (groupDetails != null)
                {
                    _dispatcherQueue.TryEnqueue(async () =>
                    {
                        if (GroupNameBox != null) GroupNameBox.Text = groupDetails.Name;
                        if (GroupMemberCount != null) GroupMemberCount.Text = $"{groupDetails.Members.Count} members";

                        bool isAdmin = (groupDetails.CreatorId == _currentUserId);

                        if (GroupAdminPanel != null) GroupAdminPanel.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;
                        if (GroupCloseButton != null) GroupCloseButton.Visibility = isAdmin ? Visibility.Collapsed : Visibility.Visible;
                        if (GroupRemovePhotoButton != null)
                            GroupRemovePhotoButton.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;
                        if (GroupUploadPhotoButton != null) GroupUploadPhotoButton.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;
                        if (GroupSaveButton != null) GroupSaveButton.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;

                        if (GroupNameBox != null)
                        {
                            GroupNameBox.Text = groupDetails.Name;
                            GroupNameBox.IsReadOnly = !isAdmin;
                            GroupNameBox.BorderThickness = isAdmin ? new Thickness(1) : new Thickness(0);
                        }

                        _currentGroupAvatarBase64 = groupDetails.AvatarData ?? "";

                        if (!string.IsNullOrEmpty(_currentGroupAvatarBase64))
                        {
                            var bitmap = await Base64ToBitmap(_currentGroupAvatarBase64);
                            if (GroupAvatarImage != null)
                            {
                                GroupAvatarImage.Source = bitmap;
                                GroupAvatarImage.Visibility = Visibility.Visible;
                            }
                            if (GroupInitials != null) GroupInitials.Visibility = Visibility.Collapsed;
                        }
                        else
                        {
                            if (GroupAvatarImage != null) GroupAvatarImage.Source = null;
                            if (GroupInitials != null)
                            {
                                GroupInitials.Visibility = Visibility.Visible;
                                GroupInitials.Text = groupDetails.Name.Substring(0, 1).ToUpper();
                            }
                        }

                        if (groupDetails.Members == null) groupDetails.Members = new List<UserItemModel>();
                        foreach (var m in groupDetails.Members)
                        {
                            m.IsSelected = isAdmin && (m.Id != _currentUserId);
                            if (!string.IsNullOrEmpty(m.AvatarData)) m.ProfileImage = await Base64ToBitmap(m.AvatarData);
                        }

                        if (GroupMembersListView != null) GroupMembersListView.ItemsSource = groupDetails.Members;
                        if (GroupProfileOverlay != null)
                        {
                            GroupProfileOverlay.Tag = groupDetails.Id;
                            GroupProfileOverlay.Visibility = Visibility.Visible;
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Group Details Error: {ex.Message}");
            }
        }
        private async void GroupUploadPhoto_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker();
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail;
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
                picker.FileTypeFilter.Add(".jpg");
                picker.FileTypeFilter.Add(".jpeg");
                picker.FileTypeFilter.Add(".png");
                picker.FileTypeFilter.Add(".jfif");
                picker.FileTypeFilter.Add(".webp");
                var file = await picker.PickSingleFileAsync();
                if (file != null)
                {
                    _tempCropFile = file;
                    _isCroppingGroup = true;

                    using (var stream = await file.OpenReadAsync())
                    {
                        var writeableBitmap = new WriteableBitmap(1, 1);
                        await writeableBitmap.SetSourceAsync(stream);
                        ProfileCropper.Source = writeableBitmap;
                    }

                    CropOverlay.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Group Photo Error: {ex.Message}");
            }
        }
        private async void GroupSave_Click(object sender, RoutedEventArgs e)
        {
            if (GroupProfileOverlay.Tag is int groupId)
            {
                var newName = GroupNameBox.Text;
                if (string.IsNullOrEmpty(newName)) return;

                if (newName.Equals("Global Chat", StringComparison.OrdinalIgnoreCase))
                {
                    await new ContentDialog { Title = "Error", Content = "Name reserved.", PrimaryButtonText = "OK", XamlRoot = this.XamlRoot }.ShowAsync();
                    return;
                }
                if (newName.Equals(_currentUsername, StringComparison.OrdinalIgnoreCase))
                {
                    await new ContentDialog { Title = "Error", Content = "Cannot use your own login.", PrimaryButtonText = "OK", XamlRoot = this.XamlRoot }.ShowAsync();
                    return;
                }
                var conflict = _allUsersCache.FirstOrDefault(u =>
                    u.Username.Equals(newName, StringComparison.OrdinalIgnoreCase) &&
                    !(u.IsGroup && u.GroupId == groupId)); 

                if (conflict != null)
                {
                    await new ContentDialog
                    {
                        Title = "Error",
                        Content = $"The login '{newName}' is already taken by '{conflict.DisplayName}'.",
                        PrimaryButtonText = "OK",
                        XamlRoot = this.XamlRoot
                    }.ShowAsync();
                    return;
                }

                string flagForLocal = "KEEP_OLD";
                if (_currentGroupAvatarBase64 == "DELETE") flagForLocal = "DELETE";
                else if (!string.IsNullOrEmpty(_currentGroupAvatarBase64)) flagForLocal = _currentGroupAvatarBase64;

                await UpdateGroupInList(groupId, newName, flagForLocal);

                string? dataForServer = null;
                if (_currentGroupAvatarBase64 == "DELETE") dataForServer = "";
                else if (!string.IsNullOrEmpty(_currentGroupAvatarBase64)) dataForServer = _currentGroupAvatarBase64;

                var parameters = new Dictionary<string, string>
            {
                { "groupId", groupId.ToString() },
                { "name", newName }
            };

                await _networkClient.SendMessageAsync(new ProtocolMessage
                {
                    Type = MessageType.UpdateGroupProfile,
                    Data = dataForServer,
                    Parameters = parameters
                });

                _currentGroupAvatarBase64 = "";
                GroupProfileOverlay.Visibility = Visibility.Collapsed;
            }
        }
        private void HandleGroupDeleted(ProtocolMessage message)
        {
            if (!int.TryParse(message.Data, out int groupId)) return;

            _dispatcherQueue.TryEnqueue(() =>
            {
                var groupInCache = _allUsersCache.FirstOrDefault(u => u.IsGroup && u.GroupId == groupId);
                if (groupInCache != null)
                {
                    _allUsersCache.Remove(groupInCache);
                }
                var groupInUi = _usersList.FirstOrDefault(u => u.IsGroup && u.GroupId == groupId);
                if (groupInUi != null)
                {
                    _usersList.Remove(groupInUi);
                }

                if (_currentChatTarget == groupInCache?.Username ||
                   (_currentChatTarget != null && groupInUi != null && _currentChatTarget == groupInUi.Username))
                {
                    LogoButton_Click(null, null); 
                    ShowNotification("System", "This group has been deleted.", false);
                }

                _isRefreshingUserList = false;
                RefreshUserListUI();
            });
        }

        private async Task ShowPreview()
        {
            var bitmap = await Base64ToBitmap(_currentAvatarBase64);
            EditAvatarImage.Source = bitmap;
            EditAvatarPreview.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        }
        private void RemovePhoto_Click(object sender, RoutedEventArgs e)
        {
            _currentAvatarBase64 = "";
            EditAvatarImage.Source = null;
            EditAvatarPreview.Fill = new SolidColorBrush(ParseColor(_currentColor));
        }

        private async void UploadPhoto_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker();
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail;
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
                picker.FileTypeFilter.Add(".jpg");
                picker.FileTypeFilter.Add(".jpeg");
                picker.FileTypeFilter.Add(".png");
                picker.FileTypeFilter.Add(".webp");
                picker.FileTypeFilter.Add(".jfif");
                var file = await picker.PickSingleFileAsync();
                if (file != null)
                {
                    _tempCropFile = file;
                    _isCroppingGroup = false;

                    using (var stream = await file.OpenReadAsync())
                    {
                        var writeableBitmap = new WriteableBitmap(1, 1);
                        await writeableBitmap.SetSourceAsync(stream);
                        ProfileCropper.Source = writeableBitmap;
                    }

                    CropOverlay.Visibility = Visibility.Visible;
                }
            }
            catch { }
        }

        private async void ConfirmCrop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using var memoryStream = new Windows.Storage.Streams.InMemoryRandomAccessStream();

                await ProfileCropper.SaveAsync(memoryStream, CommunityToolkit.WinUI.Controls.BitmapFileFormat.Png);

                memoryStream.Seek(0);
                var reader = new DataReader(memoryStream.GetInputStreamAt(0));
                await reader.LoadAsync((uint)memoryStream.Size);
                var bytes = new byte[memoryStream.Size];
                reader.ReadBytes(bytes);

                var base64 = Convert.ToBase64String(bytes);

                var bitmap = new BitmapImage();
                memoryStream.Seek(0);
                await bitmap.SetSourceAsync(memoryStream);

                if (_isCroppingGroup)
                {
                    _currentGroupAvatarBase64 = base64; 

                    if (GroupAvatarImage != null)
                    {
                        GroupAvatarImage.Source = bitmap;
                        GroupAvatarImage.Visibility = Visibility.Visible;
                    }
                    if (GroupInitials != null) GroupInitials.Visibility = Visibility.Collapsed;
                }
                else
                {
                    _currentAvatarBase64 = base64; 

                    if (EditAvatarImage != null)
                    {
                        EditAvatarImage.Source = bitmap;
                        EditAvatarImage.Visibility = Visibility.Visible;
                    }
                    if (EditAvatarPreview != null)
                        EditAvatarPreview.Fill = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Crop Error: {ex.Message}");
            }
            finally
            {
                CropOverlay.Visibility = Visibility.Collapsed;
                _tempCropFile = null;
            }
        }

        private void CancelCrop_Click(object sender, RoutedEventArgs e)
        {
            CropOverlay.Visibility = Visibility.Collapsed;
            ProfileCropper.Source = null;
        }

        private async Task<BitmapImage?> Base64ToBitmap(string base64)
        {
            if (string.IsNullOrEmpty(base64)) return null;
            string cacheKey = base64.Length > 100 ? base64.Substring(0, 50) + base64.Length : base64;
            if (_memoryImageCache.TryGetValue(cacheKey, out var cachedImage))
            {
                return cachedImage;
            }

            try
            {
                byte[] bytes = Convert.FromBase64String(base64);
                var image = new BitmapImage();
                var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                using (var writer = new Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0)))
                {
                    writer.WriteBytes(bytes);
                    await writer.StoreAsync();
                }
                stream.Seek(0);
                await image.SetSourceAsync(stream);
                if (!_memoryImageCache.ContainsKey(cacheKey))
                {
                    _memoryImageCache[cacheKey] = image;
                }
                return image;
            }
            catch (FormatException)
            {
                Console.WriteLine("[ERROR] Base64 string is corrupted/truncated.");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Bitmap conversion failed: {ex.Message}");
                return null;
            }
        }

        private async void SaveProfile_Click(object sender, RoutedEventArgs e)
        {
            var newName = EditUsernameBox.Text.Trim();
            var newBio = EditBioBox.Text.Trim();
            if (string.IsNullOrEmpty(newName)) return;
            if (newName.Equals("Global Chat", StringComparison.OrdinalIgnoreCase) ||
                newName.Equals("System", StringComparison.OrdinalIgnoreCase))
            {
                await new ContentDialog { Title = "Error", Content = "Name reserved.", PrimaryButtonText = "OK", XamlRoot = this.XamlRoot }.ShowAsync();
                return;
            }
            if (MyUsernameText != null) MyUsernameText.Text = newName;
            if (MyAvatarInitials != null && newName.Length > 0)
                MyAvatarInitials.Text = newName.Substring(0, 1).ToUpper();
            if (_networkClient != null && _networkClient.IsConnected)
            {
                var parameters = new Dictionary<string, string>
        {
            { "username", newName },
            { "bio", newBio },
            { "color", _currentColor }
        };

                await _networkClient.SendMessageAsync(new ProtocolMessage
                {
                    Type = MessageType.UpdateProfile,
                    Data = _currentAvatarBase64,
                    Parameters = parameters
                });
            }

            if (EditProfileOverlay != null) EditProfileOverlay.Visibility = Visibility.Collapsed;
        }
        private void InitializeUserList()
        {
            _allUsersCache.Clear();
            var globalChat = new UserItemModel { Username = "Global Chat", AvatarColor = "#25D366", IsSelected = false };
            
            try
            {
                var globalIcon = new BitmapImage(new Uri("ms-appx:///Assets/global_icon.png"));
                globalChat.ProfileImage = globalIcon;
            }
            catch
            {
            }
            
            _allUsersCache.Add(globalChat);
            RefreshUserListUI();
            if (UsersListView != null) UsersListView.SelectedItem = globalChat;
        }

        private async void HandleUsersList(ProtocolMessage message)
        {
            if (string.IsNullOrEmpty(message.Data)) return;
            try
            {
                var incomingUsers = System.Text.Json.JsonSerializer.Deserialize<List<UserItemModel>>(message.Data);

                if (incomingUsers != null)
                {
                    _dispatcherQueue.TryEnqueue(async () =>
                    {
                        foreach (var incoming in incomingUsers)
                        {
                            if (incoming.Username == _currentUsername) continue;

                            var existingUser = _allUsersCache.FirstOrDefault(u => u.Username == incoming.Username);

                            if (existingUser != null)
                            {
                                existingUser.DisplayName = incoming.DisplayName;
                                existingUser.Bio = incoming.Bio;
                                existingUser.AvatarColor = incoming.AvatarColor;
                                
                                if (existingUser.Username == "Global Chat")
                                {
                                    if (existingUser.ProfileImage == null)
                                    {
                                        try
                                        {
                                            var globalIcon = new BitmapImage(new Uri("ms-appx:///Assets/global_icon.png"));
                                            existingUser.ProfileImage = globalIcon;
                                        }
                                        catch { }
                                    }
                                }
                                else if (existingUser.AvatarData != incoming.AvatarData)
                                {
                                    existingUser.AvatarData = incoming.AvatarData;
                                    if (!string.IsNullOrEmpty(incoming.AvatarData))
                                        existingUser.ProfileImage = await Base64ToBitmap(incoming.AvatarData);
                                    else
                                        existingUser.ProfileImage = null;
                                }
                            }
                            else
                            {
                                incoming.IsSelected = false;
                                if (string.IsNullOrEmpty(incoming.DisplayName)) incoming.DisplayName = incoming.Username;
                                if (!string.IsNullOrEmpty(incoming.AvatarData))
                                    incoming.ProfileImage = await Base64ToBitmap(incoming.AvatarData);
                                _allUsersCache.Add(incoming);
                            }
                        }
                        RefreshUserListUI();
                        RefreshMessagesDisplayNames();
                        
                        if (_isScrolledToBottom)
                        {
                            _dispatcherQueue.TryEnqueue(ScrollToBottom);
                        }
                        _ = Task.Run(async () =>
                        {
                            if (_networkClient?.IsConnected == true)
                            {
                                await _networkClient.SendMessageAsync(new ProtocolMessage { Type = MessageType.GetHistory });
                            }
                            
                            foreach (var user in _allUsersCache)
                            {
                                if (user.Username == _currentUsername || user.Username == "Global Chat") continue;
                                await Task.Delay(50);
                                if ((_currentChatTarget == null && user.Username == "Global Chat") || 
                                    (_currentChatTarget != null && user.Username == _currentChatTarget)) continue;
                                
                                await Task.Delay(50); 
                                
                                if (_networkClient?.IsConnected == true)
                                {
                                    if (user.IsGroup)
                                    {
                                        await _networkClient.SendMessageAsync(new ProtocolMessage
                                        {
                                            Type = MessageType.GetGroupHistory,
                                            Parameters = new Dictionary<string, string> { { "groupId", user.GroupId.ToString() } }
                                        });
                                    }
                                    else if (user.Username != "Global Chat")
                                    {
                                        await _networkClient.SendMessageAsync(new ProtocolMessage
                                        {
                                            Type = MessageType.GetPrivateHistory,
                                            Parameters = new Dictionary<string, string> { { "otherUsername", user.Username } }
                                        });
                                    }
                                    else
                                    {
                                        await _networkClient.SendMessageAsync(new ProtocolMessage
                                        {
                                            Type = MessageType.GetPrivateHistory,
                                            Parameters = new Dictionary<string, string> { { "otherUsername", user.Username } }
                                        });
                                    }
                                }
                            }
                        });
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling users list: {ex.Message}");
            }
        }
        private async void HandleNewUserRegistered(ProtocolMessage message)
        {
            if (string.IsNullOrEmpty(message.Data)) return;
            try
            {
                var newUser = System.Text.Json.JsonSerializer.Deserialize<UserItemModel>(message.Data);
                if (newUser != null && newUser.Username != _currentUsername)
                {
                    _dispatcherQueue.TryEnqueue(async () =>
                    {
                        if (newUser.Username == _currentUsername) return;
                        if (_allUsersCache.Any(u => u.Username == newUser.Username)) return;
                        newUser.AvatarColor = string.IsNullOrEmpty(newUser.AvatarColor) ? "#0088CC" : newUser.AvatarColor;
                        if (!string.IsNullOrEmpty(newUser.AvatarData))
                            newUser.ProfileImage = await Base64ToBitmap(newUser.AvatarData);
                        _allUsersCache.Add(newUser);
                        if (!string.IsNullOrEmpty(UserSearchBox.Text))
                        {
                            RefreshUserListUI(); 
                        }
                    });
                }
            }
            catch { }
        }

        private void UpdateUserItemInPlace(UserItemModel existing, UserItemModel newData)
        { 
            if (existing.DisplayName != newData.DisplayName) existing.DisplayName = newData.DisplayName;
            if (existing.Bio != newData.Bio) existing.Bio = newData.Bio;
            if (existing.AvatarColor != newData.AvatarColor) existing.AvatarColor = newData.AvatarColor;
            if (existing.UnreadCount != newData.UnreadCount) existing.UnreadCount = newData.UnreadCount;
            if (existing.LastMessage != newData.LastMessage) existing.LastMessage = newData.LastMessage;
            if (existing.AvatarData != newData.AvatarData)
            {
                existing.AvatarData = newData.AvatarData;
                existing.ProfileImage = newData.ProfileImage;
            }
            if (existing.IsSelected != newData.IsSelected) existing.IsSelected = newData.IsSelected;
        }

        private void RefreshUserListUI()
        {
            if (_isRefreshingUserList) return;
            _isRefreshingUserList = true;

            try
            {
                bool isSearchMode = !string.IsNullOrEmpty(_searchQuery) ||
                            UserSearchBox.FocusState != FocusState.Unfocused;

                IEnumerable<UserItemModel> source;

                if (isSearchMode)
                {
                    if (string.IsNullOrEmpty(_searchQuery))
                    {
                        source = _allUsersCache;
                    }
                    else
                    {
                        source = _allUsersCache.Where(u =>
                            !u.IsDeleted && 
                            (
                                u.Username == "Global Chat" ||
                                (u.Username != null && u.Username.ToLower().Contains(_searchQuery)) ||
                                (u.DisplayName != null && u.DisplayName.ToLower().Contains(_searchQuery))
                            )
                        );
                    }
                }
                else
                {
                    source = _allUsersCache.Where(u =>
                      u.Username == "Global Chat" ||
                      !string.IsNullOrEmpty(u.LastMessage) ||
                      u.IsGroup
                  );
                }
                var targetList = source
                    .OrderByDescending(u => u.Username == "Global Chat")
                    .ThenByDescending(u => u.LastMessageTime)
                    .ToList();
                for (int i = _usersList.Count - 1; i >= 0; i--)
                {
                    var item = _usersList[i];
                    if (!targetList.Any(x => x.Username == item.Username)) _usersList.RemoveAt(i);
                }

                for (int i = 0; i < targetList.Count; i++)
                {
                    var newData = targetList[i];
                    if (i < _usersList.Count)
                    {
                        var currentAtPos = _usersList[i];
                        if (currentAtPos.Username != newData.Username)
                        {
                            var existingIndex = -1;
                            for (int j = i + 1; j < _usersList.Count; j++)
                            {
                                if (_usersList[j].Username == newData.Username) { existingIndex = j; break; }
                            }

                            if (existingIndex != -1)
                            {
                                _usersList.Move(existingIndex, i);
                                UpdateUserItemInPlace(_usersList[i], newData);
                            }
                            else
                            {
                                _usersList.Insert(i, newData);
                            }
                        }
                        else
                        {
                            UpdateUserItemInPlace(currentAtPos, newData);
                        }
                    }
                    else
                    {
                        _usersList.Add(newData);
                    }
                }
                string targetName = _currentChatTarget ?? "Global Chat";
                var itemToSelect = _usersList.FirstOrDefault(u => u.Username == targetName);
                if (itemToSelect != null)
                {
                    // Ensure only the correct item is selected
                    if (UsersListView.SelectedItem != itemToSelect)
                    {
                        UsersListView.SelectedItem = itemToSelect;
                    }
                }
                else if (UsersListView.SelectedItem != null)
                {
                    // Clear selection if the selected item is no longer in the list
                    UsersListView.SelectedItem = null;
                }
                
                // Clear any hover states after refresh
                ClearHoverStates();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Refresh Error: {ex.Message}");
            }
            finally
            {
                _isRefreshingUserList = false;
            }
        }

        private void UserSearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            RefreshUserListUI();
        }

        private async void UserSearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            await Task.Delay(200);
            RefreshUserListUI();
        }

        private void HandleSearchHistoryResponse(ProtocolMessage message)
        {
            try
            {
                var results = System.Text.Json.JsonSerializer.Deserialize<List<MessageViewModel>>(message.Data);

                _dispatcherQueue.TryEnqueue(() =>
                {
                    if (results != null && results.Count > 0)
                    {
                        foreach (var msg in results)
                        {
                            if (string.IsNullOrEmpty(msg.SenderUsername))
                            {
                                if (msg.IsOwnMessage) msg.SenderUsername = _currentDisplayName;
                                else msg.SenderUsername = _currentChatTarget ?? "User";
                            }
                        }

                        SearchResultsHeader.Text = $"Found {results.Count} matches on server";
                        ChatSearchResultsList.ItemsSource = results;
                        ChatSearchResultsBorder.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        SearchResultsHeader.Text = "No matches found on server";
                        ChatSearchResultsList.ItemsSource = null;
                        ChatSearchResultsBorder.Visibility = Visibility.Visible;
                    }
                });
            }
            catch { }
        }

        private void UserSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = UserSearchBox.Text.Trim();

            _searchQuery = query.ToLower();

            if (string.IsNullOrEmpty(query))
            {
                _isRefreshingUserList = true;
                _usersList.Clear();
                foreach (var user in _allUsersCache)
                {
                    _usersList.Add(user);
                }
                _isRefreshingUserList = false;
                return;
            }
            RefreshUserListUI();

            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(500);
            _searchDebounceTimer.Tick += (s, args) =>
            {
                _searchDebounceTimer.Stop();
                if (_networkClient?.IsConnected == true)
                {
                    _networkClient.SendMessageAsync(new ProtocolMessage
                    {
                        Type = MessageType.SearchUsers,
                        Data = query
                    });
                }
            };
            _searchDebounceTimer.Start();
        }
        private void HandleSearchUsersResponse(ProtocolMessage message)
        {
            if (string.IsNullOrEmpty(message.Data)) return;
            try
            {
                var foundUsers = System.Text.Json.JsonSerializer.Deserialize<List<UserItemModel>>(message.Data);
                _dispatcherQueue.TryEnqueue(() =>
                {
                    if (string.IsNullOrEmpty(UserSearchBox.Text)) return;
                    string query = UserSearchBox.Text.Trim().ToLower();

                    _usersList.Clear();

                    if (foundUsers != null)
                    {
                        foreach (var user in foundUsers)
                        {
                            if (user.Username == _currentUsername) continue;
                            bool matchLogin = user.Username != null && user.Username.ToLower().Contains(query);
                            bool matchName = user.DisplayName != null && user.DisplayName.ToLower().Contains(query);
                            if (!matchLogin && !matchName) continue;

                            var existing = _allUsersCache.FirstOrDefault(u => u.Username == user.Username);
                            if (existing != null)
                            {
                                _usersList.Add(existing);
                            }
                            else
                            {
                                if (string.IsNullOrEmpty(user.AvatarData)) user.ProfileImage = null;

                                if (!string.IsNullOrEmpty(user.AvatarData))
                                {
                                    _ = Task.Run(async () =>
                                    {
                                        var bmp = await Base64ToBitmap(user.AvatarData);
                                        _dispatcherQueue.TryEnqueue(() => user.ProfileImage = bmp);
                                    });
                                }
                                _allUsersCache.Add(user);
                                _usersList.Add(user);
                            }
                        }
                    }
                });
            }
            catch { }
        }
        private void ToggleChatSearch_Click(object sender, RoutedEventArgs e)
        {
            if (ChatSearchBox.Visibility == Visibility.Collapsed)
            {
                ChatSearchBox.Visibility = Visibility.Visible;
                ChatSearchIcon.Glyph = "\uE711"; 
                ChatSearchBox.Focus(FocusState.Keyboard);
            }
            else
            {
                CloseChatSearch();
            }
        }

        private async void SearchDebounceTimer_Tick(object? sender, object e)
        {
            _searchDebounceTimer.Stop();    
            string query = ChatSearchBox.Text.Trim();
            if (string.IsNullOrEmpty(query)) return;

            if (_networkClient != null && _networkClient.IsConnected)
            {
                var msg = new ProtocolMessage
                {
                    Type = MessageType.SearchHistory,
                    Data = query,
                    Parameters = new Dictionary<string, string>()
                };
                if (_currentChatTarget != null)
                {
                    var activeChat = _allUsersCache.FirstOrDefault(u => u.Username == _currentChatTarget);

                    if (activeChat != null && activeChat.IsGroup)
                    {
                        msg.Parameters.Add("groupId", activeChat.GroupId.ToString());
                    }
                    else
                    {
                        msg.Parameters.Add("targetUsername", _currentChatTarget);
                    }
                }
                await _networkClient.SendMessageAsync(msg);
            }
        }

        private void CloseChatSearch()
        {
            ChatSearchBox.Text = "";
            ChatSearchBox.Visibility = Visibility.Collapsed;
            ChatSearchResultsBorder.Visibility = Visibility.Collapsed;
            ChatSearchIcon.Glyph = "\uE721";
        }

        private void ChatSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = ChatSearchBox.Text.Trim();

            if (string.IsNullOrEmpty(query))
            {
                ChatSearchResultsBorder.Visibility = Visibility.Collapsed;
                _searchDebounceTimer.Stop(); 
                return;
            }
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        private async void ChatSearchResult_Click(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is MessageViewModel msg)
            {
                ChatSearchResultsBorder.Visibility = Visibility.Collapsed;
                var localItem = MessagesListView.Items
                    .Cast<MessageViewModel>()
                    .FirstOrDefault(m => m.Id == msg.Id);

                if (localItem != null)
                {
                    MessagesListView.ScrollIntoView(localItem, ScrollIntoViewAlignment.Leading);
                }
                else
                {
                    Console.WriteLine("[JUMP] Message not found locally. Requesting context from server...");
                    _pendingJumpMessageId = msg.Id;
                    _messages.Clear();
                    _filteredMessages.Clear();
                    _isScrolledToBottom = false; 
                    _isLoadingHistory = true;
                    _isFutureFullyLoaded = false;
                    var parameters = new Dictionary<string, string>();

                    if (_currentChatTarget != null)
                    {
                        var chatUser = _allUsersCache.FirstOrDefault(u => u.Username == _currentChatTarget);
                        if (chatUser != null && chatUser.IsGroup)
                        {
                            parameters.Add("groupId", chatUser.GroupId.ToString());
                        }
                        else
                        {
                            parameters.Add("targetUsername", _currentChatTarget);
                        }
                    }

                    await _networkClient.SendMessageAsync(new ProtocolMessage
                    {
                        Type = MessageType.GetHistoryAroundId,
                        Data = msg.Id.ToString(),
                        Parameters = parameters
                    });
                }
            }
        }

        private async void UsersListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            await CancelRecordingIfActive();
            if (_isRefreshingUserList) return;
            
            if (UsersListView.SelectedItem == null)
            {
                if (_usersList.Count == 0) return;

                _isRefreshingUserList = true;

                var targetUsername = _currentChatTarget ?? "Global Chat";
                var itemToRestore = _usersList.FirstOrDefault(u => u.Username == targetUsername);
                if (itemToRestore != null)
                {
                    UsersListView.SelectedItem = itemToRestore;
                }

                _isRefreshingUserList = false;
                return;
            }

            if (UsersListView.SelectedItem is UserItemModel selectedUser)
            {
                if (!string.IsNullOrEmpty(UserSearchBox.Text))
                {
                    bool exists = _allUsersCache.Any(u => u.Username == selectedUser.Username);
                    if (!exists && selectedUser.Username != "Global Chat")
                    {
                        selectedUser.LastMessageTime = DateTime.UtcNow;
                        _allUsersCache.Add(selectedUser);
                    }
                    UserSearchBox.Text = "";
                    _dispatcherQueue.TryEnqueue(async () =>
                    {
                        await Task.Delay(50);
                        ResortUserList();
                    });
                }
                bool isGlobal = selectedUser.Username == "Global Chat";
                if (!isGlobal && _currentChatTarget == selectedUser.Username) return;
                selectedUser.UnreadCount = 0;
                if (MessageTextBox != null) MessageTextBox.Text = string.Empty;
                CloseChatSearch();
                CancelReply_Click(null, null);
                _messages.Clear();
                _filteredMessages.Clear();
                _isFutureFullyLoaded = true;
                _isLoadingHistory = false;
                _isLoadingFuture = false;
                if (UserSearchBox != null) UserSearchBox.Text = "";
                MarkChatAsRead(selectedUser.Username);

                if (isGlobal)
                {
                    _currentChatTarget = null;
                    CurrentChatTitle.Text = "Global Chat";
                    _ = _networkClient?.SendMessageAsync(new ProtocolMessage { Type = MessageType.GetHistory });
                    SendReadReceipt("Global Chat");
                }
                else
                {
                    _currentChatTarget = selectedUser.Username;
                    CurrentChatTitle.Text = selectedUser.DisplayName;

                    if (selectedUser.IsGroup)
                    {
                        _ = _networkClient?.SendMessageAsync(new ProtocolMessage
                        {
                            Type = MessageType.GetGroupHistory,
                            Parameters = new Dictionary<string, string> { { "groupId", selectedUser.GroupId.ToString() } }
                        });
                    }
                    else
                    {
                        _ = _networkClient?.SendMessageAsync(new ProtocolMessage
                        {
                            Type = MessageType.GetPrivateHistory,
                            Parameters = new Dictionary<string, string> { { "otherUsername", selectedUser.Username } }
                        });
                    }
                    SendReadReceipt(selectedUser.Username);
                }

                ClearHoverStates();
            }
        }

        private void UsersList_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Move && UsersListView != null)
            {
                var selectedItem = UsersListView.SelectedItem as UserItemModel;
                if (selectedItem != null)
                {
                    if (UsersListView.SelectedItem != selectedItem)
                    {
                        UsersListView.SelectedItem = selectedItem;
                    }
                    
                    _dispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, async () =>
                    {
                        await Task.Delay(10); 
                        ClearHoverStates();
                    });
                }
            }
        }

        private void ClearHoverStates()
        {
            _dispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (UsersListView != null)
                {
                    try
                    {
                        var containers = new List<Microsoft.UI.Xaml.Controls.ListViewItem>();
                        for (int i = 0; i < UsersListView.Items.Count; i++)
                        {
                            var container = UsersListView.ContainerFromIndex(i) as Microsoft.UI.Xaml.Controls.ListViewItem;
                            if (container != null)
                            {
                                containers.Add(container);
                            }
                        }

                        var selectedContainer = UsersListView.ContainerFromItem(UsersListView.SelectedItem) as Microsoft.UI.Xaml.Controls.ListViewItem;
                        foreach (var container in containers)
                        {
                            if (container != selectedContainer)
                            {
                                Microsoft.UI.Xaml.VisualStateManager.GoToState(container, "Normal", false);
                                Microsoft.UI.Xaml.VisualStateManager.GoToState(container, "Unselected", false);
                            }
                            else if (container == selectedContainer)
                            {
                                Microsoft.UI.Xaml.VisualStateManager.GoToState(container, "Selected", false);
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            });
        }

        private void MarkChatAsRead(string username)
        {
            foreach (var msg in _messages)
            {
                if (msg.IsOwnMessage && msg.MessageStatus == "sent")
                {
                    msg.MessageStatus = "read";
                }
            }
        }

        private void HandleMessageReceived(ProtocolMessage message)
        {
            if (HandleEditDeleteAction(message)) return;
            var sender = message.Parameters?.GetValueOrDefault("sender") ?? "Unknown";
            var isOwn = sender == _currentUsername;
            var isSystem = message.Parameters?.GetValueOrDefault("isSystem") == "true";
            DateTime.TryParse(message.Parameters?.GetValueOrDefault("timestamp"), out var time);
            if (time == default) time = DateTime.UtcNow;
            string rawContent = message.Data ?? "";
            string contentPreview = GetContentPreview(rawContent); 
            string finalPreview = contentPreview;
            if (isOwn)
            {
                finalPreview = $"You: {contentPreview}";
            }
            else if (!isSystem)
            {
                finalPreview = $"{sender}: {contentPreview}";
            }
            bool isGlobalOpen = _currentChatTarget == null || _currentChatTarget == "Global Chat";
            bool shouldIncrement = !isOwn && !isGlobalOpen;
            if (!isSystem)
            {
                UpdateLastMessage("Global Chat", finalPreview, time, true, shouldIncrement);
            }
            AddMessageToView(message, isOwn: isOwn, isPrivateMessage: false);
            if (!isOwn && !isSystem)
            {
                ShowNotification("Global Chat", finalPreview, false);
            }

            if (!isOwn && !isSystem && isGlobalOpen)
            {
                SendReadReceipt("Global Chat");
            }
            
        }

        private void HandlePrivateMessageReceived(ProtocolMessage message)
        {
            if (HandleEditDeleteAction(message)) return;

            var sender = message.Parameters?.GetValueOrDefault("sender") ?? "Unknown";
            var isOwn = sender == _currentUsername;
            DateTime.TryParse(message.Parameters?.GetValueOrDefault("timestamp"), out var time);
            if (time == default) time = DateTime.UtcNow;
            if (!isOwn)
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    var existingUser = _allUsersCache.FirstOrDefault(u => u.Username == sender);
                    if (existingUser == null)
                    {
                        var newUser = new UserItemModel
                        {
                            Username = sender,
                            DisplayName = sender,
                            AvatarColor = "#0088CC", 
                            UnreadCount = 0,
                            IsSelected = false,
                            LastMessageTime = time
                        };

                        _allUsersCache.Add(newUser);
                        if (string.IsNullOrEmpty(_searchQuery))
                        {
                            _usersList.Add(newUser);
                            ResortUserList();
                        }
                    }
                });
            }
            var isSystem = message.Parameters?.GetValueOrDefault("isSystem") == "true";
            DateTime.TryParse(message.Parameters?.GetValueOrDefault("timestamp"), out time);
            if (time == default) time = DateTime.UtcNow;

            bool shouldAdd = false;
            if (isOwn) shouldAdd = (_currentChatTarget != null);
            else shouldAdd = (_currentChatTarget == sender);

            if (shouldAdd)
            {
                AddMessageToView(message, isOwn: isOwn, isPrivateMessage: true);
            }

            var rawContent = message.Data ?? "";
            string contentPreview = GetContentPreview(rawContent);
            var vm = new MessageViewModel { Content = rawContent };
            string preview = GetContentPreview(rawContent);
            if (!isSystem)
            {
                bool shouldIncrement = !isOwn && (_currentChatTarget != sender);

                if (isOwn && _currentChatTarget != null)
                {
                    UpdateLastMessage(_currentChatTarget, $"You: {preview}", time, true, false);
                }
                else
                {
                    UpdateLastMessage(sender, preview, time, true, shouldIncrement);
                }
            }
            if (!isOwn && _currentChatTarget == sender)
            {
                    SendReadReceipt(sender);
            }
            if (!isOwn && _currentChatTarget != sender)
            {
                var user = _allUsersCache.FirstOrDefault(u => u.Username == sender);
                if (user != null)
                {
                    user.UnreadCount++;
                    RefreshUserListUI();
                }
            }
            if (!isOwn && !isSystem)
            {
                ShowNotification(sender, contentPreview, true);
            }
            CheckAndLoadSticker(vm);
            _messages.Add(vm);
        }
        private void UpdateLastMessage(string username, string messageContent, DateTime? messageTime = null, bool updateTimestamp = true, bool incrementUnread = false)
        {
            if (string.IsNullOrEmpty(username) ||
                (messageContent != null && (messageContent.Contains("registered at") || messageContent.Contains("isSystem"))))
                return;

            _dispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.High, () =>
            {
                try
                {
                    string preview = messageContent ?? "No messages";
                    if (preview.StartsWith("<VOICE:")) preview = "🎤 Voice Message";
                    else if (preview.StartsWith("<STICKER:")) preview = "🧩 Sticker";
                    else if (preview.StartsWith("<GIF:")) preview = "GIF";
                    else if (preview.StartsWith("<IMG_REF:") || preview.StartsWith("<IMAGE:")) preview = "📷 Image";
                    else if (preview.StartsWith("<FILE:") || preview.StartsWith("<FILE_REF:")) preview = "📄 File";
                    if (preview.Length > 50) preview = preview.Substring(0, 47) + "...";
                    var userInList = _usersList.FirstOrDefault(u => u.Username == username);
                    var userInCache = _allUsersCache.FirstOrDefault(u => u.Username == username);
                    if (userInList == null && string.IsNullOrEmpty(_searchQuery) && userInCache != null)
                    {
                        _usersList.Add(userInCache);
                        userInList = userInCache;
                    }

                    Action<UserItemModel> updateData = (u) =>
                    {
                        u.LastMessage = preview;
                        if (updateTimestamp)
                        {
                            u.LastMessageTime = messageTime ?? DateTime.UtcNow;
                        }
                        if (incrementUnread)
                        {
                            u.UnreadCount++;
                        }
                    };

                    if (userInCache != null) updateData(userInCache);
                    if (userInList != null && userInList != userInCache) updateData(userInList);
                    if (userInList != null && string.IsNullOrEmpty(_searchQuery))
                    {
                        ResortUserList();
                    }
                }
                catch { }
            });
        }

        private void ResortUserList()
        {
            try
            {
                var globalChat = _usersList.FirstOrDefault(u => u.Username == "Global Chat");
                var sortedOthers = _usersList
                    .Where(u => u.Username != "Global Chat")
                    .OrderByDescending(u => u.LastMessageTime)
                    .ToList();
                var idealList = new List<UserItemModel>();
                if (globalChat != null) idealList.Add(globalChat);
                idealList.AddRange(sortedOthers);
                for (int i = 0; i < idealList.Count; i++)
                {
                    var item = idealList[i];
                    int currentIdx = _usersList.IndexOf(item);

                    if (currentIdx != i && currentIdx != -1)
                    {
                        _usersList.Move(currentIdx, i);
                    }
                }
            }
            catch { }
        }

        private bool HandleEditDeleteAction(ProtocolMessage message)
        {
            var action = message.Parameters?.GetValueOrDefault("action");
            if (string.IsNullOrEmpty(action)) return false;
            if (!int.TryParse(message.Parameters?.GetValueOrDefault("messageId"), out var id)) return false;

            _dispatcherQueue.TryEnqueue(async () =>
            {
                string newContent = message.Data ?? "";
                var existingMessages = _messages.Where(m => m.Id == id).ToList();
                string? chatTarget = existingMessages.FirstOrDefault()?.ChatTarget;

                if (string.IsNullOrEmpty(chatTarget))
                {
                    if (message.Parameters.ContainsKey("groupId") && int.TryParse(message.Parameters["groupId"], out int gid))
                    {
                        var group = _allUsersCache.FirstOrDefault(u => u.IsGroup && u.GroupId == gid);
                        chatTarget = group?.Username;
                    }
                    else if (message.Parameters.ContainsKey("isPrivate"))
                    {
                        var sender = message.Parameters.GetValueOrDefault("sender");
                        if (sender != _currentUsername)
                        {
                            chatTarget = sender;
                        }
                        else if (_currentChatTarget != null)
                        {
                            chatTarget = _currentChatTarget;
                        }
                    }
                }
                if (string.IsNullOrEmpty(chatTarget)) chatTarget = "Global Chat";
                if (action == "delete")
                {
                    if (existingMessages.Any())
                    {
                        foreach (var msg in existingMessages)
                        {
                            _messages.Remove(msg);
                            _filteredMessages.Remove(msg);
                        }
                        await Task.Delay(20);
                        var lastMsg = _messages.Where(m => !m.IsSystemMessage && m.SenderUsername != "System").OrderByDescending(m => m.SentAt).FirstOrDefault();
                        if (lastMsg != null)
                        {
                            UpdateLastMessage(chatTarget, lastMsg.Content, lastMsg.SentAt);
                        }
                        else
                        {
                            UpdateLastMessage(chatTarget, "No messages", DateTime.MinValue);
                        }
                    }
                    else
                    {
                        string currentActive = _currentChatTarget ?? "Global Chat";
                        bool isViewingChat = (chatTarget == currentActive);
                        if (!isViewingChat)
                        {
                            UpdateLastMessage(chatTarget, "Message deleted", DateTime.UtcNow);
                        }
                    }
                }
                else if (action == "edit")
                {
                    if (existingMessages.Any())
                    {
                        foreach (var msg in existingMessages)
                        {
                            if (!string.IsNullOrEmpty(newContent) && msg.Content != newContent)
                            {
                                msg.Content = newContent;
                                msg.EditedAt = DateTime.UtcNow;
                            }
                        }
                        var first = existingMessages.First();
                        UpdateLastMessage(chatTarget, newContent, first.SentAt, false);
                    }
                    else
                    {
                        UpdateLastMessage(chatTarget, newContent, null, false);
                    }
                }
                foreach (var msg in _messages)
                {
                    if (msg.ReplyToId == id)
                    {
                        if (action == "delete") msg.ReplyToContent = "Deleted message";
                        else if (action == "edit") msg.ReplyToContent = newContent;
                    }
                }
                if (_replyingToMessage != null && _replyingToMessage.Id == id)
                {
                    if (action == "delete")
                    {
                        CancelReply_Click(null, null);
                        ShowNotification("System", "Message you were replying to was deleted.", false);
                    }
                    else if (action == "edit")
                    {
                        _replyingToMessage.Content = newContent;
                        string preview = newContent;
                        if (preview.StartsWith("<IMAGE")) preview = "📷 Photo";
                        else if (preview.StartsWith("<FILE")) preview = "📄 File";
                        else if (preview.StartsWith("<VOICE")) preview = "🎤 Voice Message";
                        else if (preview.StartsWith("<STICKER")) preview = "🧩 Sticker";
                        if (ReplyPreviewText != null) ReplyPreviewText.Text = preview;
                    }
                }
            });

            return true;
        }

        private void HandleHistoryResponse(ProtocolMessage message)
        {
            if (string.IsNullOrEmpty(message.Data))
            {
                _isLoadingHistory = false;
                return;
            }

            try
            {
                var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var rawHistory = System.Text.Json.JsonSerializer.Deserialize<List<MessageViewModel>>(message.Data, options);
                var targetUsername = message.Parameters?.GetValueOrDefault("otherUsername");
                var isGlobalChat = targetUsername == null;
                bool isChatOpen = (isGlobalChat && _currentChatTarget == null) ||
                                  (!isGlobalChat && _currentChatTarget == targetUsername);
                if (isChatOpen)
                {
                    _dispatcherQueue.TryEnqueue(async () =>
                    {
                        bool isPaginationUp = _messages.Count > 0 && rawHistory.Any(m => m.SentAt < _messages.First().SentAt);
                        bool isPaginationDown = _messages.Count > 0 && rawHistory.Any(m => m.SentAt > _messages.Last().SentAt);

                        if (rawHistory != null && rawHistory.Count > 0)
                        {
                            if (_isLoadingFuture)
                            {
                                if (rawHistory == null || rawHistory.Count == 0)
                                {
                                    _isFutureFullyLoaded = true;
                                    Console.WriteLine("Future fully loaded.");
                                }
                                else
                                {
                                    bool allDuplicates = rawHistory.All(newMsg => _messages.Any(m => m.Id == newMsg.Id));
                                    if (allDuplicates) _isFutureFullyLoaded = true;
                                }
                            }
                            if (!isPaginationUp && !isPaginationDown)
                            {
                                _messages.Clear();
                                _filteredMessages.Clear();
                            }
                            var batchList = new List<MessageViewModel>();
                            foreach (var rawMsg in rawHistory)
                            {
                                var content = (rawMsg.Content ?? "").Trim();
                                rawMsg.SenderLogin = rawMsg.SenderUsername; 
                                rawMsg.Content = content;
                                rawMsg.ChatTarget = isGlobalChat ? null : targetUsername;
                                var user = _allUsersCache.FirstOrDefault(u => u.Username == rawMsg.SenderLogin);
                                if (user != null)
                                {
                                    rawMsg.SenderUsername = user.DisplayName;
                                }
                                else if (rawMsg.SenderLogin == _currentUsername)
                                {
                                    rawMsg.SenderUsername = _currentDisplayName;
                                }
                                if (content.StartsWith("<STICKER:") && content.EndsWith(">"))
                                {
                                    rawMsg.IsSticker = true;
                                    rawMsg.IsImage = false;
                                    rawMsg.IsFile = false;
                                    CheckAndLoadSticker(rawMsg);
                                }
                                else if (content.StartsWith("<VOICE:") && content.EndsWith(">"))
                                {
                                    try
                                    {
                                        rawMsg.VoiceData = content.Substring(7, content.Length - 8);
                                        rawMsg.IsVoice = true;
                                        rawMsg.Content = "🎤 Voice message";
                                    }
                                    catch { }
                                }
                                else if (content.StartsWith("<GIF:") && content.EndsWith(">"))
                                {
                                    rawMsg.IsGif = true;
                                    var start = content.IndexOf(":") + 1;
                                    var end = content.LastIndexOf(">");
                                    if (start > 0 && end > start)
                                    {
                                        var filename = content.Substring(start, end - start);
                                        if (_gifPreviewCache.TryGetValue(filename, out var cached))
                                        {
                                            rawMsg.GifData = cached;
                                            rawMsg.DisplayImage = await Base64ToBitmap(cached);
                                        }
                                        else if (_networkClient?.IsConnected == true)
                                        {
                                            _ = _networkClient.SendMessageAsync(new ProtocolMessage { Type = MessageType.GetGif, Data = filename });
                                        }
                                    }
                                }
                                if (rawMsg.IsImage && rawMsg.BlobId.HasValue && rawMsg.DisplayImage == null)
                                {
                                    _ = Task.Run(async () =>
                                    {
                                        await Task.Delay(100);
                                        if (_networkClient != null && _networkClient.IsConnected)
                                        {
                                            await _networkClient.SendMessageAsync(new ProtocolMessage
                                            {
                                                Type = MessageType.GetFileContent,
                                                Data = rawMsg.BlobId.Value.ToString()
                                            });
                                        }
                                    });
                                }
                                rawMsg.IsOwnMessage = string.Equals(rawMsg.SenderLogin, _currentUsername, StringComparison.OrdinalIgnoreCase);
                                if (rawMsg.IsOwnMessage)
                                {
                                    rawMsg.Status = MessageStatusEnum.Sent;
                                    if (rawMsg.IsRead || rawMsg.MessageStatus == "read")
                                    {
                                        rawMsg.Status = MessageStatusEnum.Read;
                                    }
                                }
                                rawMsg.ChatTarget = isGlobalChat ? null : targetUsername;
                                CheckAndLoadSticker(rawMsg);
                                if (!_messages.Any(m => m.Id == rawMsg.Id))
                                {
                                    batchList.Add(rawMsg);
                                }
                            }
                            if (isPaginationUp)
                            {
                                var sortedBatch = batchList.OrderBy(m => m.SentAt).ToList();
                                for (int i = 0; i < sortedBatch.Count; i++)
                                {
                                    _messages.Insert(i, sortedBatch[i]);
                                    _filteredMessages.Insert(i, sortedBatch[i]);
                                }
                            }
                            else if (isPaginationDown)
                            {
                                var sortedBatch = batchList.OrderBy(m => m.SentAt).ToList();
                                foreach (var item in sortedBatch)
                                {
                                    _messages.Add(item);
                                    _filteredMessages.Add(item);
                                }
                            }
                            else
                            {
                                foreach (var item in batchList)
                                {
                                    _messages.Add(item);
                                    _filteredMessages.Add(item);
                                }
                                await Task.Delay(50);
                                if (_pendingJumpMessageId.HasValue)
                                {
                                    var targetMsg = _messages.FirstOrDefault(m => m.Id == _pendingJumpMessageId.Value);
                                    if (targetMsg != null)
                                    {
                                        MessagesListView.UpdateLayout();
                                        MessagesListView.ScrollIntoView(targetMsg, ScrollIntoViewAlignment.Leading);
                                        await Task.Delay(100);
                                        MessagesListView.ScrollIntoView(targetMsg, ScrollIntoViewAlignment.Leading);
                                    }
                                    _pendingJumpMessageId = null;
                                    _isScrolledToBottom = false;
                                }
                                else
                                {
                                    ScrollToBottom();
                                    _isScrolledToBottom = true;
                                }
                            }
                        }
                        _isLoadingHistory = false;
                        _isLoadingFuture = false;
                    });
                }
                else
                {
                    _isLoadingHistory = false;
                    _isLoadingFuture = false;
                }
                if (rawHistory != null && rawHistory.Count > 0)
                {
                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        var lastMsg = rawHistory
                            .Where(m => !m.IsSystemMessage && m.SenderUsername != "System")
                            .OrderByDescending(m => m.SentAt)
                            .FirstOrDefault();

                        if (lastMsg != null)
                        {
                            string chatToUpdate = isGlobalChat ? "Global Chat" : targetUsername;
                            if (!string.IsNullOrEmpty(chatToUpdate))
                            {
                                var content = lastMsg.Content ?? "";
                                bool isPrivate = !isGlobalChat;
                                bool isOwn = string.Equals(lastMsg.SenderUsername, _currentUsername, StringComparison.OrdinalIgnoreCase);
                                string senderName = lastMsg.SenderUsername;
                                if (!isOwn)
                                {
                                    var senderUser = _allUsersCache.FirstOrDefault(u => u.Username == lastMsg.SenderUsername);
                                    if (senderUser != null) senderName = senderUser.DisplayName;
                                }
                                string formattedPreview = FormatPreviewText(senderName, isOwn, content, isPrivate);
                                var userItem = _allUsersCache.FirstOrDefault(u => u.Username == chatToUpdate);
                                bool isNewer = userItem == null || lastMsg.SentAt >= userItem.LastMessageTime;
                                bool isEdit = userItem != null && Math.Abs((lastMsg.SentAt - userItem.LastMessageTime).TotalSeconds) < 2 && lastMsg.EditedAt.HasValue;
                                if (isNewer || isEdit)
                                {
                                    UpdateLastMessage(chatToUpdate, formattedPreview, lastMsg.SentAt, true, false);
                                }
                            }
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"History Error: {ex.Message}");
                _isLoadingHistory = false;
            }
        }



        private void AddMessageToView(ProtocolMessage msg, bool isOwn, bool isPrivateMessage = false)
        {
            DateTime.TryParse(msg.Parameters?.GetValueOrDefault("timestamp"), out var time);
            if (time == default) time = DateTime.UtcNow;
            int.TryParse(msg.Parameters?.GetValueOrDefault("messageId"), out var id);
            var isSystem = msg.Parameters?.GetValueOrDefault("isSystem") == "true";
            var sender = msg.Parameters?.GetValueOrDefault("sender") ?? "Unknown";
            var rawContent = (msg.Data ?? "").Trim();
            string senderDisplayName = sender;
            int? replyToId = null;
            string? replySender = null;
            string? replyContent = null;

            if (msg.Parameters != null && msg.Parameters.ContainsKey("replyToId"))
            {
                if (int.TryParse(msg.Parameters["replyToId"], out int rId))
                {
                    replyToId = rId;
                    var repliedMsg = _messages.FirstOrDefault(m => m.Id == rId);
                    if (repliedMsg != null)
                    {
                        replySender = repliedMsg.SenderUsername;
                        replyContent = repliedMsg.Content;
                    }
                    else
                    {
                        replySender = "User";
                        replyContent = "Loading...";
                    }
                }
            }

            if (isOwn)
            {
                senderDisplayName = _currentDisplayName;
            }
            else
            {
                var user = _allUsersCache.FirstOrDefault(u => u.Username == sender);
                if (user != null) senderDisplayName = user.DisplayName;
            }

            bool shouldAddToView = false;
            if (isPrivateMessage)
            {
                if (isOwn) shouldAddToView = (_currentChatTarget != null);
                else shouldAddToView = (_currentChatTarget == sender);
            }
            else
            {
                shouldAddToView = (_currentChatTarget == null);
            }
            if (!shouldAddToView)
            {
                return;
            }
            var vm = new MessageViewModel
            {
                Id = id,
                SenderLogin = sender,
                SenderUsername = senderDisplayName,
                SentAt = time,
                IsOwnMessage = isOwn,
                IsSystemMessage = isSystem,
                MessageStatus = isOwn ? (msg.Parameters?.GetValueOrDefault("status") ?? "sent") : "sent",
                ChatTarget = isPrivateMessage ? _currentChatTarget : null,
                Content = rawContent,
                BlobId = null,
                ReplyToId = replyToId,
                ReplyToSender = replySender,
                ReplyToContent = replyContent,
                Status = MessageStatusEnum.Sent
            };
            CheckAndLoadSticker(vm);
            vm.Content = rawContent;
            if (vm.IsGif && rawContent.StartsWith("<GIF:") && rawContent.EndsWith(">"))
            {
                var start = rawContent.IndexOf(":") + 1;
                var end = rawContent.LastIndexOf(">");
                if (start > 0 && end > start)
                {
                    var filename = rawContent.Substring(start, end - start);
                    if (_gifPreviewCache.TryGetValue(filename, out var cached))
                    {
                        vm.GifData = cached;
                        _ = Task.Run(async () =>
                        {
                            var bmp = await Base64ToBitmap(cached);
                            _dispatcherQueue.TryEnqueue(() => vm.DisplayImage = bmp);
                        });
                    }
                    else if (_networkClient?.IsConnected == true)
                    {
                        _ = Task.Run(async () => await _networkClient.SendMessageAsync(new ProtocolMessage { Type = MessageType.GetGif, Data = filename }));
                    }
                }
            }
            CheckAndLoadSticker(vm);
            _dispatcherQueue.TryEnqueue(() =>
            {
                if (isOwn)
                {
                    var existing = _messages.LastOrDefault(m => m.IsOwnMessage && m.Id < 0);

                    if (existing != null)
                    {

                        existing.Id = id;
                        existing.Status = MessageStatusEnum.Sent;
                        existing.MessageStatus = vm.MessageStatus;
                        existing.Content = vm.Content;
                        if (vm.BlobId.HasValue) existing.BlobId = vm.BlobId;
                        if (!string.IsNullOrEmpty(vm.FileName)) existing.FileName = vm.FileName;
                        if (existing.IsFile) existing.FileSizeDisplay = vm.FileSizeDisplay;
                        return;
                    }
                }
                _messages.Add(vm);
                if (vm.BlobId.HasValue && !isOwn)
                {
                    var ext = System.IO.Path.GetExtension(vm.FileName ?? "").ToLower();
                    bool isImg = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".jfif", ".webp" }.Contains(ext);

                    var settings = uchat.Services.SettingsService.GetForUser(_currentUsername);
                    bool shouldDownload = false;

                    if (isImg) shouldDownload = vm.DisplayImage == null;
                    else shouldDownload = settings.IsAutoDownloadEnabled;

                    if (shouldDownload)
                    {
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(50);
                            if (_networkClient != null && _networkClient.IsConnected)
                            {
                                await _networkClient.SendMessageAsync(new ProtocolMessage
                                {
                                    Type = MessageType.GetFileContent,
                                    Data = vm.BlobId.Value.ToString()
                                });
                            }
                        });
                    }
                }

                if (string.IsNullOrEmpty(_searchQuery))
                {
                    _filteredMessages.Add(vm);
                }

                if (isOwn || _isScrolledToBottom)
                {
                    _ = Task.Delay(50).ContinueWith(_ => _dispatcherQueue.TryEnqueue(ScrollToBottom));
                }

                if (!_isScrolledToBottom && !isOwn)
                {
                    _hasNewMessages = true;
                    UpdateGoToNewButtonVisibility();
                }
            });
        }

        private void RenderMessageText(RichTextBlock richTextBlock, MessageViewModel msg)
        {
            richTextBlock.Blocks.Clear();
            if (msg.ShowText != Visibility.Visible || string.IsNullOrEmpty(msg.Content)) return;
            var paragraph = new Paragraph();
            ParseAndAddFormattedText(paragraph, msg.Content);

            richTextBlock.Blocks.Add(paragraph);
        }

        private void ParseAndAddFormattedText(Paragraph paragraph, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var formattingRegex = new System.Text.RegularExpressions.Regex(
                @"(\*\*(?<bold>.+?)\*\*)|(\*(?<italic>[^*]+?)\*)|(~~(?<strike>.+?)~~)|(https?://[^\s]+|www\.[^\s]+)|(@[\w\d_]+)",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            
            var matches = formattingRegex.Matches(text);
            int lastIndex = 0;
            
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                if (match.Index > lastIndex)
                {
                    string plainText = text.Substring(lastIndex, match.Index - lastIndex);
                    paragraph.Inlines.Add(new Run { Text = plainText });
                }
                
                if (match.Groups["bold"].Success)
                {
                    var run = new Run 
                    { 
                        Text = match.Groups["bold"].Value,
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold
                    };
                    paragraph.Inlines.Add(run);
                }
                else if (match.Groups["italic"].Success)
                {
                    var run = new Run 
                    { 
                        Text = match.Groups["italic"].Value,
                        FontStyle = Windows.UI.Text.FontStyle.Italic
                    };
                    paragraph.Inlines.Add(run);
                }
                else if (match.Groups["strike"].Success)
                {
                    var run = new Run 
                    { 
                        Text = match.Groups["strike"].Value,
                        TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough
                    };
                    paragraph.Inlines.Add(run);
                }
                else if (match.Value.StartsWith("http") || match.Value.StartsWith("www."))
                {
                    try
                    {
                        string uriString = match.Value.StartsWith("www.") ? "https://" + match.Value : match.Value;
                        var hyperlink = new Hyperlink { NavigateUri = new Uri(uriString) };
                        hyperlink.Inlines.Add(new Run
                        {
                            Text = match.Value,
                            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 181, 246))
                        });
                        paragraph.Inlines.Add(hyperlink);
                    }
                    catch
                    {
                        paragraph.Inlines.Add(new Run { Text = match.Value });
                    }
                }
                else if (match.Value.StartsWith("@"))
                {
                    string matchText = match.Value;
                    string targetUsername = matchText.Substring(1);
                    var taggedUser = _allUsersCache.FirstOrDefault(u => u.Username.Equals(targetUsername, StringComparison.OrdinalIgnoreCase));
                    if (taggedUser == null && targetUsername.Equals(_currentUsername, StringComparison.OrdinalIgnoreCase))
                    {
                        taggedUser = new UserItemModel
                        {
                            Username = _currentUsername,
                            DisplayName = _currentDisplayName,
                            Bio = _currentBio,
                            AvatarColor = _currentColor,
                            ProfileImage = MyProfileImage?.Source
                        };
                    }

                    var btn = new HyperlinkButton
                    {
                        Content = matchText,
                        Padding = new Thickness(0),
                        Margin = new Thickness(0, 0, 0, -4),
                        BorderThickness = new Thickness(0),
                        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 181, 246)),
                        FontSize = 14,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        IsHitTestVisible = true,
                        ClickMode = ClickMode.Press,
                    };
                    if (taggedUser != null)
                    {
                        btn.Click += (s, args) => OpenUserProfile(taggedUser);
                    }
                    else
                    {
                        btn.Click += (s, args) =>
                        {
                            string login = matchText.StartsWith("@") ? matchText.Substring(1) : matchText;
                            var userNow = _allUsersCache.FirstOrDefault(u => u.Username.Equals(login, StringComparison.OrdinalIgnoreCase));

                            if (userNow != null)
                            {
                                OpenUserProfile(userNow);
                            }
                            else if (login.Equals(_currentUsername, StringComparison.OrdinalIgnoreCase))
                            {
                                var me = new UserItemModel
                                {
                                    Username = _currentUsername,
                                    DisplayName = _currentDisplayName,
                                    Bio = _currentBio,
                                    AvatarColor = _currentColor,
                                    ProfileImage = MyProfileImage?.Source
                                };
                                OpenUserProfile(me);
                            }
                            else
                            {
                                ShowUserNotFoundTooltip(btn);
                            }
                        };
                    }

                    var container = new InlineUIContainer { Child = btn };
                    paragraph.Inlines.Add(container);
                }

                lastIndex = match.Index + match.Length;
            }
            
            if (lastIndex < text.Length)
            {
                paragraph.Inlines.Add(new Run { Text = text.Substring(lastIndex) });
            }
        }

        private void MessageRichTextBlock_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
        {
            if (sender is RichTextBlock rtb && args.NewValue is MessageViewModel msg)
            {
                RenderMessageText(rtb, msg);
                msg.PropertyChanged += (s, e) =>
                {
                    if ((e.PropertyName == nameof(MessageViewModel.Content) || e.PropertyName == nameof(MessageViewModel.ShowText))
                        && rtb.DataContext == msg)
                    {
                        _dispatcherQueue.TryEnqueue(() => RenderMessageText(rtb, msg));
                    }
                };
            }
        }

        private void ShowUserNotFoundTooltip(FrameworkElement targetElement)
        {
            var flyout = new Flyout
            {
                Content = new TextBlock
                {
                    Text = "User not found",
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                    Margin = new Thickness(10)
                },
                Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Top
            };
            var style = new Style(typeof(FlyoutPresenter));
            style.Setters.Add(new Setter(FlyoutPresenter.BackgroundProperty,
                new SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 30, 30))));
            style.Setters.Add(new Setter(FlyoutPresenter.BorderBrushProperty,
                new SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 60, 60))));
            flyout.FlyoutPresenterStyle = style;

            flyout.ShowAt(targetElement);
        }

        private void MessageTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            CheckForMentions();
            UpdateMessageTextBoxHeight();
        }

        private void UpdateMessageTextBoxHeight()
        {
            
        }

        private void CheckForMentions()
        {
            if (MessageTextBox == null || SuggestionsBorder == null || SuggestionsList == null) return;
            int cursorPosition = MessageTextBox.SelectionStart;
            string text = MessageTextBox.Text;
            if (cursorPosition == 0)
            {
                SuggestionsBorder.Visibility = Visibility.Collapsed;
                return;
            }
            int lastAt = text.LastIndexOf('@', cursorPosition - 1);
            if (lastAt != -1)
            {
                string query = text.Substring(lastAt + 1, cursorPosition - lastAt - 1);
                if (!query.Contains(" ") && !query.Contains("\n") && !query.Contains("\r"))
                {
                    string cleanQuery = query.ToLower();
                    var matches = _allUsersCache.Where(u => u.Username != "Global Chat").Where(u => !u.IsGroup).Where(u => !u.IsDeleted);
                    {
                        SuggestionsList.ItemsSource = matches;
                        SuggestionsBorder.Visibility = Visibility.Visible;
                        return;
                    }
                }
            }
            SuggestionsBorder.Visibility = Visibility.Collapsed;
        }

        private void SuggestionsList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is UserItemModel user)
            {
                int cursorPosition = MessageTextBox.SelectionStart;
                string text = MessageTextBox.Text;
                int lastAt = text.LastIndexOf('@', Math.Max(0, cursorPosition - 1));
                if (lastAt != -1)
                {
                    string textBefore = text.Substring(0, lastAt);
                    string textAfter = text.Substring(cursorPosition);
                    string insertedTag = $"@{user.Username} ";
                    MessageTextBox.Text = textBefore + insertedTag + textAfter;
                    MessageTextBox.SelectionStart = textBefore.Length + insertedTag.Length;
                }
                SuggestionsBorder.Visibility = Visibility.Collapsed;
                MessageTextBox.Focus(FocusState.Keyboard);
            }
        }

        private async void HandleStickerPacksList(ProtocolMessage message)
        {
            try
            {
                var packs = await Task.Run(() =>
                {
                    return System.Text.Json.JsonSerializer.Deserialize<List<StickerPackModel>>(message.Data);
                });

                if (packs != null)
                {
                    _dispatcherQueue.TryEnqueue(async () =>
                    {
                        _stickerPacks.Clear();
                        foreach (var p in packs)
                        {
                            if (!string.IsNullOrEmpty(p.Cover))
                            {
                                p.CoverImage = await Base64ToBitmap(p.Cover);
                            }

                            _stickerPacks.Add(p);
                        }

                        StickerPacksList.ItemsSource = _stickerPacks;
                        if (_stickerPacks.Count > 0) StickerPacksList.SelectedIndex = 0;
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading sticker packs list: {ex.Message}");
            }
        }
        private void HandleStickerPackContent(ProtocolMessage message)
        {
            System.Diagnostics.Debug.WriteLine($"[DEBUG] Sticker Content Received! Data: {message.Data}");
            try
            {
                var packName = message.Parameters?["packName"];
                var files = System.Text.Json.JsonSerializer.Deserialize<List<string>>(message.Data);
                if (files != null && !string.IsNullOrEmpty(packName))
                {
                    _dispatcherQueue.TryEnqueue(() =>
                    { 
                        _currentStickers.Clear();
                        StickersGrid.ItemsSource = _currentStickers;

                        foreach (var file in files)
                        {
                            var item = new StickerItemModel { FileName = file, PackName = packName };
                            _currentStickers.Add(item);
                            LoadStickerImage(item);
                        }
                        System.Diagnostics.Debug.WriteLine($"[DEBUG] Added {files.Count} items to UI.");
                    });
                }
            }
            catch { }
        }

        private async void LoadStickerImage(StickerItemModel item)
        {
            string key = item.FullPath;
            if (_stickerCache.ContainsKey(key))
            {
                item.Image = _stickerCache[key];
                var idx = _currentStickers.IndexOf(item);
                if (idx >= 0) _currentStickers[idx] = item;
                return;
            }
            await _networkClient.SendMessageAsync(new ProtocolMessage
            {
                Type = MessageType.GetSticker,
                Data = key 
            });
        }

        private void StickerButton_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is StickerItemModel sticker)
            {
                _hoveredSticker = sticker;
                
                if (_stickerPreviewTimer != null)
                {
                    _stickerPreviewTimer.Stop();
                    _stickerPreviewTimer = null;
                }
                
                _stickerPreviewTimer = new DispatcherTimer();
                _stickerPreviewTimer.Interval = TimeSpan.FromMilliseconds(400);
                _stickerPreviewTimer.Tick += (s, args) =>
                {
                    _stickerPreviewTimer.Stop();
                    if (_hoveredSticker == sticker && _hoveredSticker.Image != null)
                    {
                        ShowStickerPreview(_hoveredSticker.Image);
                    }
                };
                _stickerPreviewTimer.Start();
            }
        }
        
        private void StickerButton_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (_stickerPreviewTimer != null)
            {
                _stickerPreviewTimer.Stop();
                _stickerPreviewTimer = null;
            }
            _hoveredSticker = null;
            HideStickerPreview();
        }
        
        private void ShowStickerPreview(Microsoft.UI.Xaml.Media.ImageSource? imageSource)
        {
            if (StickerPreviewImage != null && StickerPreviewOverlay != null && imageSource != null)
            {
                StickerPreviewImage.Source = imageSource;
                StickerPreviewOverlay.Visibility = Visibility.Visible;
                
                var fadeIn = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = new Microsoft.UI.Xaml.Duration(TimeSpan.FromMilliseconds(100))
                };
                var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
                storyboard.Children.Add(fadeIn);
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fadeIn, StickerPreviewImage);
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fadeIn, "Opacity");
                storyboard.Begin();
            }
        }
        
        private void HideStickerPreview()
        {
            if (StickerPreviewImage != null)
            {
                var fadeOut = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
                {
                    From = 1,
                    To = 0,
                    Duration = new Microsoft.UI.Xaml.Duration(TimeSpan.FromMilliseconds(100))
                };
                var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
                storyboard.Children.Add(fadeOut);
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fadeOut, StickerPreviewImage);
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fadeOut, "Opacity");
                storyboard.Completed += (s, e) =>
                {
                    if (StickerPreviewOverlay != null)
                    {
                        StickerPreviewOverlay.Visibility = Visibility.Collapsed;
                    }
                    if (StickerPreviewImage != null)
                    {
                        StickerPreviewImage.Source = null;
                        StickerPreviewImage.Opacity = 0;
                    }
                };
                storyboard.Begin();
            }
            else
            {
                if (StickerPreviewOverlay != null)
                {
                    StickerPreviewOverlay.Visibility = Visibility.Collapsed;
                }
            }
        }

        private async void StickerItem_Click(object sender, RoutedEventArgs e)
        {
            HideStickerPreview();
            if (_stickerPreviewTimer != null) { _stickerPreviewTimer.Stop(); _stickerPreviewTimer = null; }

            if (sender is Button btn && btn.Tag is StickerItemModel sticker)
            {
                int? replyId = _replyingToMessage?.Id;
                string? replySender = _replyingToMessage?.SenderUsername;
                string? replyContent = _replyingToMessage?.Content;
                if (_replyingToMessage != null)
                {
                    if (_replyingToMessage.IsImage) replyContent = "📷 Photo";
                    else if (_replyingToMessage.IsFile) replyContent = "📄 File";
                    else if (_replyingToMessage.IsVoice) replyContent = "🎤 Voice Message";
                    else if (_replyingToMessage.IsSticker) replyContent = "🧩 Sticker";
                    else if (_replyingToMessage.IsGif) replyContent = "GIF";
                }
                CancelReply_Click(null, null);
                string content = $"<STICKER:{sticker.PackName}|{sticker.FileName}>";

                var localMsg = new MessageViewModel
                {
                    Id = new Random().Next(int.MinValue, 0),
                    SenderUsername = _currentUsername,
                    SentAt = DateTime.UtcNow,
                    IsOwnMessage = true,
                    ChatTarget = _currentChatTarget,
                    Content = content,
                    IsSticker = true,
                    StickerSource = sticker.Image,

                    ReplyToId = replyId,
                    ReplyToSender = replySender,
                    ReplyToContent = replyContent
                };

                _messages.Add(localMsg);
                if (string.IsNullOrEmpty(_searchQuery)) _filteredMessages.Add(localMsg);
                ScrollToBottom();

                EmojiPickerFlyout.Hide();
                await SendDirectMessageFromSticker(content, replyId);             
            }
        }

        private void OpenPrivateChat_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is MessageViewModel msg)
            {
                string targetUsername = msg.SenderLogin;
                var user = _allUsersCache.FirstOrDefault(u => u.Username == targetUsername);
                if (user == null)
                {
                    user = new UserItemModel
                    {
                        Username = targetUsername,
                        DisplayName = msg.SenderUsername, 
                        AvatarColor = "#0088CC", 
                        UnreadCount = 0,
                        IsSelected = false
                    };
                    _allUsersCache.Add(user);
                }
                if (!_usersList.Contains(user))
                {
                    _usersList.Insert(1, user); 
                }
                UsersListView.SelectedItem = user;
                UsersListView.ScrollIntoView(user);
            }
        }

        private async Task SendDirectMessageFromSticker(string content, int? replyId)
        {
            try
            {
                var parameters = new Dictionary<string, string>();
                if (replyId.HasValue) parameters.Add("replyToId", replyId.Value.ToString());

                var msg = new ProtocolMessage
                {
                    Type = MessageType.SendMessage,
                    Data = content,
                    Parameters = parameters 
                };

                var activeChat = _allUsersCache.FirstOrDefault(u => u.Username == _currentChatTarget);

                if (activeChat != null && activeChat.IsGroup)
                {
                    msg.Type = MessageType.SendGroupMessage;
                    msg.Parameters["groupId"] = activeChat.GroupId.ToString();
                    UpdateLastMessage(activeChat.Username, "🧩 Sticker", DateTime.UtcNow);
                }
                else if (_currentChatTarget != null)
                {
                    msg.Type = MessageType.SendPrivateMessage;
                    msg.Parameters["targetUsername"] = _currentChatTarget;
                    UpdateLastMessage(_currentChatTarget, "🧩 Sticker", DateTime.UtcNow);
                }
                else
                {
                    UpdateLastMessage("Global Chat", "🧩 Sticker", DateTime.UtcNow);
                }

                if (_networkClient != null && _networkClient.IsConnected)
                    await _networkClient.SendMessageAsync(msg);
            }
            catch { }
        }

        private void HandleStickerDataResponse(ProtocolMessage message)
        {
            var packName = message.Parameters?.GetValueOrDefault("packName");
            var fileName = message.Parameters?.GetValueOrDefault("fileName");
            var base64 = message.Data;
            if (packName == null || fileName == null || string.IsNullOrEmpty(base64)) return;
            string key = $"{packName}|{fileName}";
            _dispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    var bitmap = await Base64ToBitmap(base64);
                    if (bitmap == null) return;
                    _stickerCache[key] = bitmap;
                    foreach (var msg in _messages)
                    {
                        if (msg.IsSticker && msg.Content.Contains(key) && msg.StickerSource == null)
                        {
                            msg.StickerSource = bitmap;
                        }
                    }
                    var menuItem = _currentStickers.FirstOrDefault(s => s.FullPath == key);
                    if (menuItem != null) menuItem.Image = bitmap;
                    for (int i = 0; i < _messages.Count; i++)
                    {
                        var msg = _messages[i];
                        if (!string.IsNullOrEmpty(msg.Content) &&
                            msg.Content.Contains(fileName, StringComparison.OrdinalIgnoreCase))
                        {
                            msg.IsSticker = true;
                            msg.StickerSource = bitmap;
                            msg.DisplayImage = bitmap;
                            msg.IsImage = false;
                            msg.IsFile = false;
                            msg.IsVoice = false;
                            msg.IsGif = false;
                            msg.OnPropertyChanged(nameof(MessageViewModel.DisplayImage));
                            msg.OnPropertyChanged(nameof(MessageViewModel.IsImage));
                            msg.OnPropertyChanged(nameof(MessageViewModel.ShowImageLoading)); 
                            msg.OnPropertyChanged(nameof(MessageViewModel.ShowText));     
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Sticker Update Error: {ex.Message}");
                }
            });
        }
        private void CheckAndLoadSticker(MessageViewModel vm)
        {
            if (!string.IsNullOrEmpty(vm.Content) && vm.Content.StartsWith("<STICKER:") && vm.Content.EndsWith(">"))
            {
                vm.IsSticker = true;
                vm.IsImage = false; 
                vm.IsFile = false;

                try
                {
                    var raw = vm.Content;
                    var inner = raw.Substring(9, raw.Length - 10); 

                    if (_stickerCache.ContainsKey(inner))
                    {
                        vm.StickerSource = _stickerCache[inner];
                    }
                    else if (_networkClient?.IsConnected == true)
                    {
                        _ = Task.Run(() => _networkClient.SendMessageAsync(new ProtocolMessage
                        {
                            Type = MessageType.GetSticker,
                            Data = inner
                        }));
                    }
                }
                catch { }
            }
        }

        private async void StickerPack_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is StickerPackModel pack)
            {
                if (SelectPackText != null) SelectPackText.Visibility = Visibility.Collapsed;
                _currentStickers.Clear();
                System.Diagnostics.Debug.WriteLine($"[DEBUG] Asking content for pack: {pack.Name}");

                if (_networkClient != null && _networkClient.IsConnected)
                {
                    await _networkClient.SendMessageAsync(new ProtocolMessage
                    {
                        Type = MessageType.GetStickerPackContent,
                        Data = pack.Name
                    });
                }
            }
        }

        private void EmojiListGrid_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is string emoji)
            {
                int selectionIndex = MessageTextBox.SelectionStart;
                string currentText = MessageTextBox.Text ?? "";
                string newText = currentText.Insert(selectionIndex, emoji);
                MessageTextBox.Text = newText;
                MessageTextBox.SelectionStart = selectionIndex + emoji.Length;
                MessageTextBox.Focus(FocusState.Keyboard);
            }
        }

        private void ReplyContext_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is MessageViewModel msg)
            {
                _replyingToMessage = msg; 

                if (ReplyPanel != null)
                {
                    ReplyPanel.Visibility = Visibility.Visible;
                    if (ReplyTargetName != null)
                        ReplyTargetName.Text = msg.SenderUsername ?? "Unknown";

                    if (ReplyPreviewText != null)
                    {
                        string preview = msg.Content ?? "";

                        if (msg.IsSticker) preview = "🧩 Sticker";
                        else if (msg.IsImage) preview = "📷 Photo";
                        else if (msg.IsVoice) preview = "🎤 Voice Message";
                        else if (msg.IsFile) preview = "📄 File";
                        else if (msg.IsGif) preview = "GIF";

                        if (preview.StartsWith("<") && preview.EndsWith(">"))
                        {
                            if (preview.StartsWith("<VOICE")) preview = "🎤 Voice Message";
                            else if (preview.StartsWith("<STICKER")) preview = "🧩 Sticker";
                            else if (!msg.IsImage && !msg.IsFile && !msg.IsGif) preview = "Media content";
                        }

                        ReplyPreviewText.Text = preview;
                    }
                }
                MessageTextBox?.Focus(FocusState.Keyboard);
            }
        }
        private void CancelReply_Click(object sender, RoutedEventArgs e)
        {
            _replyingToMessage = null;
            if (ReplyPanel != null) ReplyPanel.Visibility = Visibility.Collapsed;
        }
        private void Reply_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int originalId)
            {
                var targetMsg = _messages.FirstOrDefault(m => m.Id == originalId);
                if (targetMsg != null)
                {
                    MessagesListView.ScrollIntoView(targetMsg, ScrollIntoViewAlignment.Leading);
                }
                else { }
            }
        }

        private void HandleGifListResponse(ProtocolMessage message)
        {
            if (string.IsNullOrEmpty(message.Data)) return;

            _dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    var files = System.Text.Json.JsonSerializer.Deserialize<List<string>>(message.Data);
                    if (files != null)
                    {
                        _gifList.Clear();

                        var itemsToLoad = new List<GifPickerItem>();

                        foreach (var file in files)
                        {
                            var item = new GifPickerItem { Filename = file };

                            if (_ramGifCache.TryGetValue(file, out var cachedImg))
                            {
                                item.Image = cachedImg;
                            }
                            else
                            {
                                itemsToLoad.Add(item);
                            }

                            _gifList.Add(item);
                        }

                        if (itemsToLoad.Count > 0)
                        {
                            LoadGifsInBackground(itemsToLoad);
                        }
                    }
                }
                catch { }
            });
        }

        private void LoadGifsInBackground(List<GifPickerItem> items)
        {
            _ = Task.Run(async () =>
            {
                var semaphore = new SemaphoreSlim(20);

                var tasks = items.Select(async item =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        if (_networkClient?.IsConnected == true)
                        {
                            await _networkClient.SendMessageAsync(new ProtocolMessage
                            {
                                Type = MessageType.GetGif,
                                Data = item.Filename
                            });
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);
            });
        }

        private void CancelEdit_Click(object sender, RoutedEventArgs e) => EditProfileOverlay.Visibility = Visibility.Collapsed;
        private void OnZoom(object sender, PointerRoutedEventArgs e)
        {
            var keyState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
            bool isCtrlDown = (keyState & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;

            if (isCtrlDown)
            {
                var delta = e.GetCurrentPoint(null).Properties.MouseWheelDelta;
                if (delta == 0) return;

                double zoomStep = 0.1;
                if (delta > 0) _currentZoom += zoomStep;
                else _currentZoom -= zoomStep;

                if (_currentZoom < 0.7) _currentZoom = 0.7;
                if (_currentZoom > 1.2) _currentZoom = 1.2;

                UpdateInterfaceLayout();

                e.Handled = true;
            }
        }

        private async void HandleGifReceived(ProtocolMessage message)
        {
            if (HandleEditDeleteAction(message)) return;
            var sender = message.Parameters?.GetValueOrDefault("sender") ?? "Unknown";
            var isOwn = sender == _currentUsername;
            var filename = message.Parameters?.GetValueOrDefault("filename") ?? "animation.gif";
            var gifBase64 = message.Data ?? "";
            DateTime.TryParse(message.Parameters?.GetValueOrDefault("timestamp"), out var time);
            int.TryParse(message.Parameters?.GetValueOrDefault("messageId"), out var msgId);
            if (!string.IsNullOrEmpty(gifBase64))
                _gifPreviewCache[filename] = gifBase64;
            bool isPrivateMessage = message.Parameters?.ContainsKey("targetUsername") == true;
            bool shouldAddToView = false;
            if (isPrivateMessage)
            {
                if (isOwn) shouldAddToView = (_currentChatTarget != null);
                else shouldAddToView = (_currentChatTarget == sender);
            }
            else
            {
                shouldAddToView = (_currentChatTarget == null); 
            }
            if (shouldAddToView && !isOwn)
            {
                if (isPrivateMessage && _currentChatTarget == sender)
                {
                    SendReadReceipt(sender);
                }
                else if (!isPrivateMessage && _currentChatTarget == null)
                {
                    SendReadReceipt("Global Chat");
                }
            }
            bool isPrivate = message.Parameters?.ContainsKey("targetUsername") == true;
            string previewText = FormatPreviewText(sender, isOwn, "GIF", isPrivate);
            string updateTarget = isPrivate ? (isOwn ? message.Parameters?.GetValueOrDefault("targetUsername") : sender) : "Global Chat";
            bool isChatOpen = false;
            if (isPrivate)
            {
                isChatOpen = _currentChatTarget == updateTarget;
            }
            else
            {
                isChatOpen = _currentChatTarget == null || _currentChatTarget == "Global Chat";
            }
            bool shouldIncrement = !isOwn && !isChatOpen;
            if (updateTarget != null)
            {
                UpdateLastMessage(updateTarget, previewText, time != default ? time : (DateTime?)null, true, shouldIncrement);
            }

            if (!isOwn)
            {
                string title = isPrivate ? sender : "Global Chat";
                ShowNotification(title, previewText, isPrivate);
            }
            if (!shouldAddToView && !isOwn)
            {
                string chatToUpdate = isPrivateMessage ? sender : "Global Chat";
                UpdateLastMessage(chatToUpdate, "GIF", time != default ? time : (DateTime?)null);
                var user = _allUsersCache.FirstOrDefault(u => u.Username == chatToUpdate);
                if (user != null) { user.UnreadCount++; RefreshUserListUI(); }
                return;
            }
            BitmapImage? preloadedBitmap = null;
            if (!string.IsNullOrEmpty(gifBase64))
            {
                preloadedBitmap = await Base64ToBitmap(gifBase64);
            }
            _dispatcherQueue.TryEnqueue(() =>
            {
                if (isOwn)
                {
                    if (_messages.Any(m => m.Id == msgId)) return;
                    var existing = _messages.LastOrDefault(m => m.IsOwnMessage && m.Id < 0 && m.IsGif);
                    if (existing != null)
                    {
                        existing.Id = msgId;
                        existing.SentAt = time;
                        if (existing.Status != MessageStatusEnum.Read)
                            existing.Status = MessageStatusEnum.Sent;
                        if (preloadedBitmap != null) existing.DisplayImage = preloadedBitmap;
                        return;
                    }
                }
                var vm = new MessageViewModel
                {
                    Id = msgId,
                    SenderUsername = sender,
                    SentAt = time,
                    IsOwnMessage = isOwn,
                    Status = MessageStatusEnum.Sent, 
                    ChatTarget = isPrivateMessage ? _currentChatTarget : null,
                    Content = $"<GIF:{filename}>",
                    IsGif = true,
                    GifData = gifBase64,
                    DisplayImage = preloadedBitmap
                };
                if (shouldAddToView)
                {
                    _messages.Add(vm);
                    if (string.IsNullOrEmpty(_searchQuery)) _filteredMessages.Add(vm);

                    if (isOwn || _isScrolledToBottom) ScrollToBottom();
                    else { _hasNewMessages = true; UpdateGoToNewButtonVisibility(); }
                }
            });
        }

        private async void CancelVoiceButton_Click(object sender, RoutedEventArgs e)
        {
            await CancelRecordingIfActive();
        }

        private async Task CancelRecordingIfActive()
        {
            if (_voiceManager != null && _voiceManager.IsRecording)
            {
                await _voiceManager.StopRecordingAsync();
                if (VoiceIcon != null) VoiceIcon.Glyph = "\uE720"; 
                if (VoiceButton != null)
                    VoiceButton.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
                if (CancelVoiceButton != null) CancelVoiceButton.Visibility = Visibility.Collapsed;
            }
        }

        private void MessagesScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
        {
            if (sender is ScrollViewer sv)
            {
                var verticalOffset = sv.VerticalOffset;
                var scrollableHeight = sv.ScrollableHeight;
                var viewportHeight = sv.ViewportHeight; 
                _isScrolledToBottom = (scrollableHeight - verticalOffset) < 50;
                UpdateGoToNewButtonVisibility();
                if (verticalOffset < 50 && !_isLoadingHistory && _messages.Count > 0)
                {
                    LoadMoreHistory();
                }
                if ((scrollableHeight - verticalOffset) < 50 && !_isLoadingFuture && !_isFutureFullyLoaded && _messages.Count > 0)
                {
                    LoadMoreFutureHistory();
                }
            }
        }

        private async void LoadMoreFutureHistory()
        {
            if (_networkClient == null || !_networkClient.IsConnected) return;
            var newestMessage = _messages.LastOrDefault();
            if (newestMessage == null) return;
            _isLoadingFuture = true;
            var parameters = new Dictionary<string, string>
    {
        { "afterId", newestMessage.Id.ToString() }
    };

            if (_currentChatTarget != null)
            {
                var chatUser = _allUsersCache.FirstOrDefault(u => u.Username == _currentChatTarget);

                if (chatUser != null && chatUser.IsGroup)
                {
                    parameters.Add("groupId", chatUser.GroupId.ToString());
                    await _networkClient.SendMessageAsync(new ProtocolMessage
                    {
                        Type = MessageType.GetGroupHistory,
                        Parameters = parameters
                    });
                }
                else
                {
                    parameters.Add("otherUsername", _currentChatTarget);
                    await _networkClient.SendMessageAsync(new ProtocolMessage
                    {
                        Type = MessageType.GetPrivateHistory, 
                        Parameters = parameters
                    });
                }
            }
            else
            {
                await _networkClient.SendMessageAsync(new ProtocolMessage
                {
                    Type = MessageType.GetHistory, 
                    Parameters = parameters
                });
            }
        }

        private void UpdateGoToNewButtonVisibility()
        {
            if (GoToNewButton != null)
            {
                GoToNewButton.Visibility = (_hasNewMessages && !_isScrolledToBottom) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void GoToNewButton_Click(object sender, RoutedEventArgs e) => ScrollToBottom();

        private async Task SendMessage()
        {
            if (_networkClient == null || !_networkClient.IsConnected || string.IsNullOrWhiteSpace(MessageTextBox.Text)) return;

            var text = MessageTextBox.Text;
            MessageTextBox.Text = "";
            int? currentReplyId = _replyingToMessage?.Id;
            string? currentReplySender = _replyingToMessage?.SenderUsername;
            string? currentReplyContent = _replyingToMessage?.Content;
            if (_replyingToMessage != null)
            {
                if (_replyingToMessage.IsImage) currentReplyContent = "📷 Photo";
                else if (_replyingToMessage.IsFile) currentReplyContent = "📄 File";
                else if (_replyingToMessage.IsVoice) currentReplyContent = "🎤 Voice Message";
                else if (_replyingToMessage.IsSticker) currentReplyContent = "🧩 Sticker";
                else if (_replyingToMessage.IsGif) currentReplyContent = "GIF";
            }
            CancelReply_Click(null, null);
            _isScrolledToBottom = true;
            _hasNewMessages = false;
            UserItemModel? currentTargetEntity = null;
            if (_currentChatTarget != null)
            {
                currentTargetEntity = _allUsersCache.FirstOrDefault(u => u.Username == _currentChatTarget);
            }
            var localMsg = new MessageViewModel
            {
                Id = new Random().Next(int.MinValue, 0),
                SenderUsername = _currentUsername,
                SentAt = DateTime.UtcNow,
                IsOwnMessage = true,
                IsSystemMessage = false,
                MessageStatus = "sent",
                ChatTarget = _currentChatTarget,
                Content = text,
                ReplyToId = currentReplyId,
                ReplyToSender = currentReplySender,
                ReplyToContent = currentReplyContent,
                Status = MessageStatusEnum.Sending
            };
            _messages.Add(localMsg);
            _filteredMessages.Add(localMsg);
            ScrollToBottom();
            try
            {
                var parameters = new Dictionary<string, string>();
                if (currentReplyId.HasValue) parameters.Add("replyToId", currentReplyId.Value.ToString());
                string myPreview = $"You: {text}";
                if (currentTargetEntity != null && currentTargetEntity.IsGroup)
                {
                    parameters.Add("groupId", currentTargetEntity.GroupId.ToString());

                    await _networkClient.SendMessageAsync(new ProtocolMessage
                    {
                        Type = MessageType.SendGroupMessage,
                        Data = text,
                        Parameters = parameters
                    });
                    localMsg.Status = MessageStatusEnum.Sent;
                    UpdateLastMessage(currentTargetEntity.Username, myPreview, DateTime.UtcNow);
                }
                else if (_currentChatTarget != null)
                {
                    parameters.Add("targetUsername", _currentChatTarget);

                    await _networkClient.SendMessageAsync(new ProtocolMessage
                    {
                        Type = MessageType.SendPrivateMessage,
                        Data = text,
                        Parameters = parameters
                    });
                    localMsg.Status = MessageStatusEnum.Sent;
                    UpdateLastMessage(_currentChatTarget, myPreview, DateTime.UtcNow);
                }
                else
                {
                    await _networkClient.SendMessageAsync(new ProtocolMessage
                    {
                        Type = MessageType.SendMessage,
                        Data = text,
                        Parameters = parameters.Count > 0 ? parameters : null
                    });
                    localMsg.Status = MessageStatusEnum.Sent;
                    UpdateLastMessage("Global Chat", myPreview, DateTime.UtcNow);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error sending message: {ex.Message}");
            }
        }
        private async void SendReadReceipt(string chatTarget)
        {
            if (string.IsNullOrEmpty(chatTarget)) return;
            if (_networkClient == null || !_networkClient.IsConnected) return;

            var parameters = new Dictionary<string, string>();

            if (chatTarget == "Global Chat")
            {
                parameters.Add("isGlobal", "true");
            }
            else
            {
                var chatItem = _allUsersCache.FirstOrDefault(u => u.Username == chatTarget);
                if (chatItem == null)
                {
                    parameters.Add("targetUsername", chatTarget);
                    parameters.Add("isPrivate", "true");
                }
                else if (chatItem.IsGroup)
                {
                    parameters.Add("groupId", chatItem.GroupId.ToString());
                }
                else
                {
                    parameters.Add("targetUsername", chatTarget);
                    parameters.Add("isPrivate", "true");
                }
            }

            try
            {
                await _networkClient.SendMessageAsync(new ProtocolMessage
                {
                    Type = MessageType.MessagesRead,
                    Parameters = parameters
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error sending read receipt: {ex.Message}");
            }
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e) => await SendMessage();

        private async void MessageTextBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                var ctrlState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
                bool isCtrl = (ctrlState & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;

                if (isCtrl)
                {}
                else
                {
                    e.Handled = true; 
                    await SendMessage();
                }
            }
        }

        #region Text Formatting Handlers

        private void ApplyTextFormatting(TextBox textBox, string marker)
        {
            if (textBox == null) return;
            
            int selStart = textBox.SelectionStart;
            int selLength = textBox.SelectionLength;
            string text = textBox.Text ?? "";
            
            if (selLength > 0)
            {
                string selectedText = text.Substring(selStart, selLength);
                string formattedText = marker + selectedText + marker;
                textBox.Text = text.Substring(0, selStart) + formattedText + text.Substring(selStart + selLength);
                textBox.SelectionStart = selStart + formattedText.Length;
                textBox.SelectionLength = 0;
            }
            else
            {
                textBox.Text = text.Insert(selStart, marker + marker);
                textBox.SelectionStart = selStart + marker.Length;
            }
            
            textBox.Focus(FocusState.Programmatic);
        }
        
        private void FormatBold_Click(object sender, RoutedEventArgs e)
        {
            ApplyTextFormatting(MessageTextBox, "**");
        }
        
        private void FormatItalic_Click(object sender, RoutedEventArgs e)
        {
            ApplyTextFormatting(MessageTextBox, "*");
        }
        
        private void FormatStrikethrough_Click(object sender, RoutedEventArgs e)
        {
            ApplyTextFormatting(MessageTextBox, "~~");
        }
        
        private void FormatCut_Click(object sender, RoutedEventArgs e)
        {
            if (MessageTextBox.SelectionLength > 0)
            {
                var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
                dataPackage.SetText(MessageTextBox.SelectedText);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
                
                int selStart = MessageTextBox.SelectionStart;
                string text = MessageTextBox.Text ?? "";
                MessageTextBox.Text = text.Remove(selStart, MessageTextBox.SelectionLength);
                MessageTextBox.SelectionStart = selStart;
            }
        }
        
        private void FormatCopy_Click(object sender, RoutedEventArgs e)
        {
            if (MessageTextBox.SelectionLength > 0)
            {
                var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
                dataPackage.SetText(MessageTextBox.SelectedText);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
            }
        }
        
        private async void FormatPaste_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var content = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
                if (content.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
                {
                    string pasteText = await content.GetTextAsync();
                    int selStart = MessageTextBox.SelectionStart;
                    string text = MessageTextBox.Text ?? "";
                    
                    if (MessageTextBox.SelectionLength > 0)
                    {
                        text = text.Remove(selStart, MessageTextBox.SelectionLength);
                    }
                    
                    MessageTextBox.Text = text.Insert(selStart, pasteText);
                    MessageTextBox.SelectionStart = selStart + pasteText.Length;
                }
            }
            catch { }
        }
        
        private void ApplyTextFormattingToTextBox(TextBox textBox, string marker)
        {
            ApplyTextFormatting(textBox, marker);
        }
        
        #endregion

        private void EmojiPickerFlyout_Closing(object sender, object args)
        {
            HideStickerPreview();
            if (_stickerPreviewTimer != null)
            {
                _stickerPreviewTimer.Stop();
                _stickerPreviewTimer = null;
            }
            _hoveredSticker = null;
        }
        
        private async void EmojiPickerFlyout_Opening(object sender, object e)
        {
            if (EmojiListGrid.ItemsSource == null)
            {
                EmojiListGrid.ItemsSource = uchat.Models.EmojiData.All;
            }
            if (_stickerPacks.Count == 0 && _networkClient?.IsConnected == true)
            {
                await _networkClient.SendMessageAsync(new ProtocolMessage { Type = MessageType.GetStickerPacks });
            }
            if (_gifPickerItems.Count > 0) return;
            if (_isLoadingGifs) return;
            if (_networkClient != null && _networkClient.IsConnected)
            {
                try
                {
                    _isLoadingGifs = true;
                    await _networkClient.SendMessageAsync(new ProtocolMessage { Type = MessageType.GetGifList });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error opening GIF picker: {ex.Message}");
                    _isLoadingGifs = false;
                }
            }
        }

        private async void GifItemButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                var gifFilename = button.Tag as string;
                if (string.IsNullOrEmpty(gifFilename) && button.DataContext is string filenameFromContext)
                    gifFilename = filenameFromContext;
                if (string.IsNullOrEmpty(gifFilename))
                {
                    var image = FindVisualChild<Image>(button);
                    if (image != null && image.DataContext is string imageFilename)
                        gifFilename = imageFilename;
                }
                if (string.IsNullOrEmpty(gifFilename) || _networkClient == null || !_networkClient.IsConnected)
                    return;
                EmojiPickerFlyout.Hide();
                try
                {
                    string gifBase64 = "";
                    int? replyId = _replyingToMessage?.Id;
                    string? replySender = _replyingToMessage?.SenderUsername;
                    string? replyContent = _replyingToMessage?.Content;
                    if (_replyingToMessage != null)
                    {
                        if (_replyingToMessage.IsImage) replyContent = "📷 Photo";
                        else if (_replyingToMessage.IsFile) replyContent = "📄 File";
                        else if (_replyingToMessage.IsVoice) replyContent = "🎤 Voice Message";
                        else if (_replyingToMessage.IsSticker) replyContent = "🧩 Sticker";
                        else if (_replyingToMessage.IsGif) replyContent = "GIF";
                    }

                    CancelReply_Click(null, null);
                    if (_gifPreviewCache.TryGetValue(gifFilename, out var cached))
                    {
                        gifBase64 = cached;
                    }
                    var localMsg = new MessageViewModel
                    {
                        Id = new Random().Next(int.MinValue, 0),
                        SenderUsername = _currentUsername,
                        SentAt = DateTime.UtcNow,
                        IsOwnMessage = true,
                        ChatTarget = _currentChatTarget,
                        Content = $"<GIF:{gifFilename}>",
                        IsGif = true,
                        GifData = gifBase64,
                        ReplyToId = replyId,
                        ReplyToSender = replySender,
                        ReplyToContent = replyContent
                    };
                    if (!string.IsNullOrEmpty(gifBase64))
                    {
                        localMsg.DisplayImage = await Base64ToBitmap(gifBase64);
                    }
                    _messages.Add(localMsg);
                    if (string.IsNullOrEmpty(_searchQuery)) _filteredMessages.Add(localMsg);
                    ScrollToBottom();
                    try
                    {
                        var activeChat = _allUsersCache.FirstOrDefault(u => u.Username == _currentChatTarget);
                        var parameters = new Dictionary<string, string>();
                        if (replyId.HasValue)
                        {
                            parameters.Add("replyToId", replyId.Value.ToString());
                        }

                        if (activeChat != null && activeChat.IsGroup)
                        {
                            parameters.Add("groupId", activeChat.GroupId.ToString());
                            await _networkClient.SendMessageAsync(new ProtocolMessage
                            {
                                Type = MessageType.SendGroupMessage,
                                Data = $"<GIF:{gifFilename}>",
                                Parameters = parameters 
                            });

                            UpdateLastMessage(activeChat.Username, "GIF", DateTime.UtcNow);
                        }
                        else
                        {
                            var msg = new ProtocolMessage
                            {
                                Type = MessageType.SendGif,
                                Data = gifFilename,
                                Parameters = parameters 
                            };

                            if (_currentChatTarget != null)
                            {
                                if (!msg.Parameters.ContainsKey("targetUsername"))
                                {
                                    msg.Parameters.Add("targetUsername", _currentChatTarget);
                                }
                            }
                            await _networkClient.SendMessageAsync(msg);

                            string preview = "You: GIF"; 
                            if (_currentChatTarget != null)
                                UpdateLastMessage(_currentChatTarget, preview, DateTime.UtcNow);
                            else
                                UpdateLastMessage("Global Chat", preview, DateTime.UtcNow);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error sending GIF to network: {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error sending GIF: {ex.Message}");
                }
            }
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T result)
                    return result;
                var childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                    return childOfChild;
            }
            return null;
        }

        private static T? FindChild<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T result && result.Name == name)
                    return result;
                var childOfChild = FindChild<T>(child, name);
                if (childOfChild != null)
                    return childOfChild;
            }
            return null;
        }

        private Dictionary<string, string> _gifPreviewCache = new Dictionary<string, string>(); 

        private void HandleGifDataResponse(ProtocolMessage message)
        {
            try
            {
                var filename = message.Parameters?.GetValueOrDefault("filename");
                var gifData = message.Data;
                var base64 = message.Data;
                if (string.IsNullOrEmpty(filename) || string.IsNullOrEmpty(gifData)) return;

                _gifPreviewCache[filename] = gifData;

                _dispatcherQueue.TryEnqueue(async () =>
                {
                    try
                    {
                        var bitmap = await Base64ToBitmap(gifData);
                        if (bitmap == null) return;
                        var msgsToUpdate = _messages.Where(m =>
                            m.Content.StartsWith("<GIF:") &&
                            m.Content.Contains(filename)).ToList();
                        if (!_ramGifCache.ContainsKey(filename))
                        {
                            _ramGifCache[filename] = bitmap;
                        }
                        foreach (var msg in msgsToUpdate)
                        {
                            msg.GifData = gifData;
                            msg.DisplayImage = bitmap;
                            msg.IsGif = false;
                        }
                        _dispatcherQueue.TryEnqueue(() =>
                        {
                            var item = _gifList.FirstOrDefault(x => x.Filename == filename);
                            if (item != null)
                            {
                                item.Image = bitmap;
                            }
                        });
                        await Task.Delay(20);
                        foreach (var msg in msgsToUpdate) msg.IsGif = true;
                        var pickerItem = _gifPickerItems.FirstOrDefault(x => x.Filename == filename);
                        if (pickerItem != null)
                        {
                            pickerItem.Image = bitmap;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"GIF Update Error: {ex.Message}");
                    }
                });
            }
            catch { }
        }

        private void HandleScheduledMessagesList(ProtocolMessage message)
        {
            try
            {
                List<ScheduledMessageViewModel>? scheduledMessages = null;
                if (!string.IsNullOrEmpty(message.Data))
                {
                    try { scheduledMessages = System.Text.Json.JsonSerializer.Deserialize<List<ScheduledMessageViewModel>>(message.Data); } catch { scheduledMessages = new List<ScheduledMessageViewModel>(); }
                }

                if (scheduledMessages == null || scheduledMessages.Count == 0)
                {
                    _dispatcherQueue.TryEnqueue(async () =>
                    {
                        var dialog = new ContentDialog { Title = "Planned Messages", Content = "No scheduled messages found.", PrimaryButtonText = "OK", XamlRoot = this.XamlRoot };
                        await dialog.ShowAsync();
                    });
                    return;
                }

                _dispatcherQueue.TryEnqueue(async () =>
                {
                    var targetUsername = message.Parameters?.GetValueOrDefault("targetUsername") ?? "";
                    var dialog = new PlannedMessagesDialog(scheduledMessages, targetUsername, _networkClient);
                    dialog.XamlRoot = this.XamlRoot;
                    await dialog.ShowAsync();
                });
            }
            catch { }
        }

        private void CopyMessage_Click(object sender, RoutedEventArgs e)
        {
            int id = 0;
            if (sender is MenuFlyoutItem menuItem && menuItem.Tag is int menuId)
            {
                id = menuId;
            }
            else
            {
                return;
            }

            var msg = _messages.FirstOrDefault(m => m.Id == id);
            if (msg != null)
            {
                DataPackage dataPackage = new DataPackage();
                dataPackage.SetText(msg.Content);
                Clipboard.SetContent(dataPackage);
            }
        }

        private async void EditMessage_Click(object sender, RoutedEventArgs e)
        {
            int id = 0;
            if (sender is Button btn && btn.Tag is int btnId) id = btnId;
            else if (sender is MenuFlyoutItem menuItem && menuItem.Tag is int menuId) id = menuId;
            else return;
            
            var msg = _messages.FirstOrDefault(m => m.Id == id);
            if (msg == null) return;

            FrameworkElement? targetElement = null;
            if (sender is MenuFlyoutItem menuItemSender)
            {
                var menuFlyout = menuItemSender.Parent as MenuFlyout;
                if (menuFlyout?.Target != null)
                {
                    targetElement = menuFlyout.Target as FrameworkElement;
                }
            }
            else if (sender is Button button)
            {
                targetElement = button;
            }

            string currentContent = msg.Content;
            System.Diagnostics.Debug.WriteLine($"[EditMessage] Opening edit for ID={id}, Content='{currentContent}'");
            
            var textBox = new TextBox
            {
                MinWidth = 240,
                MinHeight = 80,
                MaxHeight = 200,
                PlaceholderText = "Input text here",
                AcceptsReturn = true, 
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["MainFont"],
                Foreground = (Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["Current_TextWhite"],
                Background = (Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["Current_BackgroundDarker"],
                BorderBrush = (Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["Current_BorderDark"],
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 8, 12, 8)
            };
            
            textBox.Text = currentContent;
            
            var editContextMenu = new MenuFlyout();
            
            var boldItem = new MenuFlyoutItem { Text = "Bold", Icon = new SymbolIcon(Symbol.Bold) };
            boldItem.Click += (s, args) => ApplyTextFormatting(textBox, "**");
            editContextMenu.Items.Add(boldItem);
            
            var italicItem = new MenuFlyoutItem { Text = "Italic", Icon = new SymbolIcon(Symbol.Italic) };
            italicItem.Click += (s, args) => ApplyTextFormatting(textBox, "*");
            editContextMenu.Items.Add(italicItem);
            
            var strikeItem = new MenuFlyoutItem { Text = "Strikethrough" };
            strikeItem.Icon = new FontIcon { Glyph = "\xEDE0" };
            strikeItem.Click += (s, args) => ApplyTextFormatting(textBox, "~~");
            editContextMenu.Items.Add(strikeItem);
            
            editContextMenu.Items.Add(new MenuFlyoutSeparator());
            
            var cutItem = new MenuFlyoutItem { Text = "Cut", Icon = new SymbolIcon(Symbol.Cut) };
            cutItem.Click += (s, args) =>
            {
                if (textBox.SelectionLength > 0)
                {
                    var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
                    dataPackage.SetText(textBox.SelectedText);
                    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
                    int selStart = textBox.SelectionStart;
                    textBox.Text = textBox.Text.Remove(selStart, textBox.SelectionLength);
                    textBox.SelectionStart = selStart;
                }
            };
            editContextMenu.Items.Add(cutItem);
            
            var copyItem = new MenuFlyoutItem { Text = "Copy", Icon = new SymbolIcon(Symbol.Copy) };
            copyItem.Click += (s, args) =>
            {
                if (textBox.SelectionLength > 0)
                {
                    var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
                    dataPackage.SetText(textBox.SelectedText);
                    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
                }
            };
            editContextMenu.Items.Add(copyItem);
            
            var pasteItem = new MenuFlyoutItem { Text = "Paste", Icon = new SymbolIcon(Symbol.Paste) };
            pasteItem.Click += async (s, args) =>
            {
                try
                {
                    var content = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
                    if (content.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
                    {
                        string pasteText = await content.GetTextAsync();
                        int selStart = textBox.SelectionStart;
                        string existingText = textBox.Text ?? "";
                        if (textBox.SelectionLength > 0)
                        {
                            existingText = existingText.Remove(selStart, textBox.SelectionLength);
                        }
                        textBox.Text = existingText.Insert(selStart, pasteText);
                        textBox.SelectionStart = selStart + pasteText.Length;
                    }
                }
                catch { }
            };
            editContextMenu.Items.Add(pasteItem);
            textBox.ContextFlyout = editContextMenu;
            
            var flyoutContent = new StackPanel
            {
                Spacing = 8,
                Padding = new Thickness(8)
            };
            flyoutContent.Children.Add(textBox);

            var buttonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8
            };

            var saveButton = new Button
            {
                Content = "Save",
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Colors.Transparent),
                Foreground = (Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["Current_TextWhite"],
                BorderBrush = (Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["Current_BorderDark"],
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 8, 16, 8),
                CornerRadius = new CornerRadius(4),
                Tag = id
            };

            saveButton.Resources["ButtonBackground"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Colors.Transparent);
            saveButton.Resources["ButtonBackgroundPointerOver"] = (Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["PrimaryGreen"];
            saveButton.Resources["ButtonBackgroundPressed"] = (Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["DarkGreen"];
            saveButton.Resources["ButtonForeground"] = (Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["Current_TextWhite"];
            saveButton.Resources["ButtonForegroundPointerOver"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Colors.White);
            saveButton.Resources["ButtonForegroundPressed"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Colors.White);
            saveButton.Resources["ButtonBorderBrush"] = (Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["Current_BorderDark"];
            saveButton.Resources["ButtonBorderBrushPointerOver"] = (Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["PrimaryGreen"];

            var cancelButton = new Button
            {
                Content = "Cancel",
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Colors.Transparent),
                Foreground = (Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["Current_TextWhite"],
                BorderBrush = (Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["Current_BorderDark"],
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 8, 16, 8),
                CornerRadius = new CornerRadius(4)
            };

            buttonsPanel.Children.Add(cancelButton);
            buttonsPanel.Children.Add(saveButton);
            flyoutContent.Children.Add(buttonsPanel);

            var editFlyout = new Flyout
            {
                Content = flyoutContent,
                Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom,
                ShowMode = FlyoutShowMode.Transient
            };

            Func<Task> saveAction = async () =>
            {
                string newText = textBox.Text;
                if (string.IsNullOrWhiteSpace(newText))
                {
                    editFlyout.Hide();
                    return;
                }

                var messageToUpdate = _messages.FirstOrDefault(m => m.Id == id);
                if (messageToUpdate == null)
                {
                    editFlyout.Hide();
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"[EditMessage] Saving ID={id}, OldContent='{messageToUpdate.Content}', NewContent='{newText}'");

                messageToUpdate.Content = newText;
                messageToUpdate.EditedAt = DateTime.UtcNow;
                
                messageToUpdate.OnPropertyChanged(nameof(MessageViewModel.Content));
                messageToUpdate.OnPropertyChanged(nameof(MessageViewModel.EditedAt));
                messageToUpdate.OnPropertyChanged(nameof(MessageViewModel.EditedTimeDisplay));
                messageToUpdate.OnPropertyChanged(nameof(MessageViewModel.ShowEditedTime));
                messageToUpdate.OnPropertyChanged(nameof(MessageViewModel.TimeDisplay));
                
                var chatTarget = _currentChatTarget ?? "Global Chat";
                UpdateLastMessage(chatTarget, newText, messageToUpdate.SentAt, false);
                
                var activeChat = _allUsersCache.FirstOrDefault(u => u.Username == _currentChatTarget);
                
                if (activeChat != null && activeChat.IsGroup)
                {
                    await _networkClient!.SendMessageAsync(new ProtocolMessage
                    {
                        Type = MessageType.EditGroupMessage,
                        Data = newText,
                        Parameters = new Dictionary<string, string>
                {
                    { "messageId", id.ToString() },
                    { "groupId", activeChat.GroupId.ToString() }
                }
                    });
                }
                else
                {
                    var type = MessageType.EditMessage;
                    var parameters = new Dictionary<string, string>
                    {
                        { "messageId", id.ToString() }
                    };
                    if (_currentChatTarget != null)
                    {
                        parameters.Add("isPrivate", "true");
                    }
                    await _networkClient!.SendMessageAsync(new ProtocolMessage
                    {
                        Type = type,
                        Data = newText,
                        Parameters = parameters
                    });
                }

                editFlyout.Hide();
            };

            textBox.KeyDown += async (s, args) =>
            {
                if (args.Key == VirtualKey.Enter)
                {
                    var keyState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
                    bool isCtrlPressed = (keyState & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
                    
                    if (isCtrlPressed)
                    {
                        var tb = s as TextBox;
                        if (tb != null)
                        {
                            int selectionStart = tb.SelectionStart;
                            int selectionLength = tb.SelectionLength;
                            string currentText = tb.Text ?? "";
                            
                            if (selectionLength > 0)
                            {
                                currentText = currentText.Remove(selectionStart, selectionLength);
                            }
                            
                            tb.Text = currentText.Insert(selectionStart, "\r\n");
                            tb.SelectionStart = selectionStart + 2;
                        }
                        args.Handled = true;
                    }
                    else
                    {
                        args.Handled = true;
                        await saveAction();
                    }
                }
            };

            saveButton.Click += async (s, args) => await saveAction();

            cancelButton.Click += (s, args) =>
            {
                editFlyout.Hide();
            };

            if (targetElement != null)
            {
                FlyoutBase.SetAttachedFlyout(targetElement, editFlyout);
                FlyoutBase.ShowAttachedFlyout(targetElement);
            }
            else if (sender is FrameworkElement senderElement)
            {
                FrameworkElement? messageElement = null;
                foreach (var item in MessagesListView.Items)
                {
                    if (item is MessageViewModel vm && vm.Id == id)
                    {
                        var container = MessagesListView.ContainerFromItem(item) as FrameworkElement;
                        if (container != null)
                        {
                            messageElement = FindChild<Border>(container, "MessageBorder");
                            if (messageElement == null)
                            {
                                messageElement = container;
                            }
                            break;
                        }
                    }
                }

                if (messageElement != null)
                {
                    FlyoutBase.SetAttachedFlyout(messageElement, editFlyout);
                    FlyoutBase.ShowAttachedFlyout(messageElement);
                }
                else
                {
                    editFlyout.ShowAt(senderElement);
                }
            }

            editFlyout.Opening += (s, args) =>
            {
                textBox.Focus(FocusState.Programmatic);
                textBox.SelectAll();
            };
        }

        private async void DeleteMessage_Click(object sender, RoutedEventArgs e)
        {
            int id = 0;
            if (sender is Button btn && btn.Tag is int btnId) id = btnId;
            else if (sender is MenuFlyoutItem menuItem && menuItem.Tag is int menuId) id = menuId;
            else return;

            var message = _messages.FirstOrDefault(m => m.Id == id);
            if (message == null) return;

            var activeChat = _allUsersCache.FirstOrDefault(u => u.Username == _currentChatTarget);

            _messages.Remove(message);
            _filteredMessages.Remove(message);

            await Task.Delay(20);

            var newLastMessage = _messages.OrderByDescending(m => m.SentAt).FirstOrDefault(m => !m.IsSystemMessage);
            UpdateLastMessage(_currentChatTarget ?? "Global Chat", newLastMessage?.Content ?? "No messages", newLastMessage?.SentAt);

            if (activeChat != null && activeChat.IsGroup)
            {
                await _networkClient!.SendMessageAsync(new ProtocolMessage
                {
                    Type = MessageType.DeleteGroupMessage,
                    Parameters = new Dictionary<string, string>
            {
                { "messageId", id.ToString() },
                { "groupId", activeChat.GroupId.ToString() }
            }
                });
            }
            else if (_currentChatTarget != null)
            {
                await _networkClient!.SendMessageAsync(new ProtocolMessage
                {
                    Type = MessageType.DeletePrivateMessage,
                    Parameters = new Dictionary<string, string>
            {
                { "messageId", id.ToString() },
                { "isPrivate", "true" }
            }
                });
            }
            else
            {
                await _networkClient!.SendMessageAsync(new ProtocolMessage
                {
                    Type = MessageType.DeleteMessage,
                    Parameters = new Dictionary<string, string> { { "messageId", id.ToString() } }
                });
            }
        }

        private void OpenUserProfile(UserItemModel user)
        {
            if (user == null) return;
            if (user.Username == "Global Chat") return;
            if (user.IsGroup)
            {
                if (_networkClient != null && _networkClient.IsConnected)
                {
                    _ = _networkClient.SendMessageAsync(new ProtocolMessage
                    {
                        Type = MessageType.GetGroupDetails,
                        Data = user.GroupId.ToString()
                    });
                }
                return;
            }
            var updatedUser = _allUsersCache.FirstOrDefault(u => u.Username == user.Username);
            if (updatedUser != null) user = updatedUser;
            if (ViewUserUsername != null) { ViewUserUsername.Text = !string.IsNullOrEmpty(user.DisplayName) ? user.DisplayName : user.Username; }
            if (ViewUserBio != null) { ViewUserBio.Text = string.IsNullOrEmpty(user.Bio) ? "No bio set." : user.Bio; }
            if (ViewUserLogin != null) { ViewUserLogin.Text = $"@{user.Username}"; }
            if (ViewUserImage != null && ViewUserInitials != null && ViewUserEllipse != null)
            {
                if (user.ProfileImage != null)
                {
                    ViewUserImage.Source = user.ProfileImage;
                    ViewUserImage.Visibility = Visibility.Visible;
                    ViewUserInitials.Visibility = Visibility.Collapsed;
                }
                else
                {
                    ViewUserImage.Visibility = Visibility.Collapsed;
                    ViewUserInitials.Visibility = Visibility.Visible;
                    ViewUserInitials.Text = user.Initials;
                    try { ViewUserEllipse.Fill = new SolidColorBrush(ParseColor(user.AvatarColor)); } catch { }
                }
            }
            if (UserProfileOverlay != null) UserProfileOverlay.Visibility = Visibility.Visible;
        }

        private void ViewProfile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem menuItem && menuItem.Tag is UserItemModel user)
            {
                OpenUserProfile(user);
            }
        }

        private void ViewProfileFromMessage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is MessageViewModel msg)
            {
                var user = _allUsersCache.FirstOrDefault(u => u.Username == msg.SenderLogin);

                if (user != null)
                {
                    OpenUserProfile(user);
                }
                else
                {
                    var tempUser = new UserItemModel
                    {
                        Username = msg.SenderLogin,      
                        DisplayName = msg.SenderUsername,
                        AvatarColor = "#0088CC",         
                        Bio = "User not in your contact list."
                    };
                    OpenUserProfile(tempUser);
                }
            }
        }

        private async void DeleteChat_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.Tag is UserItemModel user)
            {
                if (user.Username == "Global Chat")
                {
                    return;
                }
                if (user.IsGroup)
                {
                    if (user.IsGroupOwner)
                    {
                        var dialog = new ContentDialog
                        {
                            Title = "Delete Group Forever?",
                            Content = $"Are you sure you want to delete '{user.DisplayName}'? This will remove the group for ALL members and delete all history.",
                            PrimaryButtonText = "Delete Group",
                            CloseButtonText = "Cancel",
                            DefaultButton = ContentDialogButton.Close,
                            XamlRoot = this.XamlRoot
                        };

                        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                        {
                            if (_networkClient != null && _networkClient.IsConnected)
                            {
                                await _networkClient.SendMessageAsync(new ProtocolMessage
                                {
                                    Type = MessageType.DeleteGroup,
                                    Data = user.GroupId.ToString()
                                });
                            }
                        }
                    }
                    else
                    {
                        var dialog = new ContentDialog
                        {
                            Title = "Leave Group",
                            Content = $"Are you sure you want to leave '{user.DisplayName}'?",
                            PrimaryButtonText = "Leave",
                            CloseButtonText = "Cancel",
                            DefaultButton = ContentDialogButton.Close,
                            XamlRoot = this.XamlRoot
                        };

                        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                        {
                            if (_networkClient != null && _networkClient.IsConnected)
                            {
                                await _networkClient.SendMessageAsync(new ProtocolMessage
                                {
                                    Type = MessageType.LeaveGroup,
                                    Data = user.GroupId.ToString()
                                });
                            }
                        }
                    }
                    return;
                }

                bool isDeletedUser = user.IsDeleted;
                bool forEveryone = false;

                if (isDeletedUser)
                {
                    var dialog = new ContentDialog
                    {
                        Title = "Delete Chat Forever?",
                        Content = "This account was deleted. Deleting this chat will erase it permanently.",
                        PrimaryButtonText = "Delete",
                        CloseButtonText = "Cancel",
                        DefaultButton = ContentDialogButton.Primary,
                        XamlRoot = this.XamlRoot
                    };

                    if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
                    forEveryone = true;
                }
                else
                {
                    var contentStack = new StackPanel();
                    contentStack.Children.Add(new TextBlock { Text = $"Are you sure you want to delete chat with {user.DisplayName}?", Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap });
                    var checkBox = new CheckBox { Content = $"Also delete for {user.DisplayName}", Margin = new Thickness(0, 5, 0, 0) };
                    contentStack.Children.Add(checkBox);

                    var dialog = new ContentDialog
                    {
                        Title = "Delete Chat",
                        Content = contentStack,
                        PrimaryButtonText = "Delete",
                        CloseButtonText = "Cancel",
                        DefaultButton = ContentDialogButton.Primary,
                        XamlRoot = this.XamlRoot
                    };

                    if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
                    forEveryone = checkBox.IsChecked ?? false;
                }

                if (_networkClient != null && _networkClient.IsConnected)
                {
                    await _networkClient.SendMessageAsync(new ProtocolMessage
                    {
                        Type = MessageType.DeleteChat,
                        Parameters = new Dictionary<string, string>
                {
                    { "targetUsername", user.Username },
                    { "forEveryone", forEveryone.ToString().ToLower() }
                }
                    });
                }

                _usersList.Remove(user);
                var cachedItem = _allUsersCache.FirstOrDefault(u => u.Username == user.Username);
                if (cachedItem != null) _allUsersCache.Remove(cachedItem);
                if (_currentChatTarget == user.Username) LogoButton_Click(null, null);
            }
            
        }



        private async void NetworkClient_ConnectionLost(object? sender, EventArgs e)
        {
            if (_isReconnecting) return;
            if (_networkClient == null) return;
            _isReconnecting = true;
            _dispatcherQueue.TryEnqueue(() =>
            {
                _isConnected = false;
                UpdateWindowTitle();
                UpdateLogoBasedOnConnectionState(); 

                if (ConnectionStatusTextBlock != null)
                {
                    ConnectionStatusTextBlock.Text = "Reconnecting...";
                    ConnectionStatusTextBlock.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 193, 7));
                    ConnectionStatusTextBlock.Visibility = Visibility.Visible;
                }

                if (MessageTextBox != null) MessageTextBox.IsEnabled = false;
                if (SendButton != null) SendButton.IsEnabled = false;
            });

            while (true)
            {
                if (_networkClient == null || string.IsNullOrEmpty(_storedPassword))
                {
                    _isReconnecting = false;
                    return;
                }

                await Task.Delay(3000); 

                try
                {
                    var reconnected = await _networkClient.ReconnectAsync(_serverIp, _serverPort);

                    if (reconnected)
                    {
                        _dispatcherQueue.TryEnqueue(async () =>
                        {
                            if (_networkClient == null) return;

                            _isConnected = true;
                            UpdateWindowTitle();
                            UpdateLogoBasedOnConnectionState();

                            if (ConnectionStatusTextBlock != null)
                            {
                                ConnectionStatusTextBlock.Text = "Connected";
                                ConnectionStatusTextBlock.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 76, 175, 80)); // Зеленый

                                await Task.Delay(2000);
                                if (_isConnected) ConnectionStatusTextBlock.Visibility = Visibility.Collapsed;
                            }

                            if (MessageTextBox != null) MessageTextBox.IsEnabled = true;
                            if (SendButton != null) SendButton.IsEnabled = true;

                            if (!string.IsNullOrEmpty(_currentUsername) && !string.IsNullOrEmpty(_storedPassword))
                            {
                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        await _networkClient.SendMessageAsync(new ProtocolMessage
                                        {
                                            Type = MessageType.Login,
                                            Parameters = new Dictionary<string, string>
                                    {
                                        { "username", _currentUsername },
                                        { "password", _storedPassword }
                                    }
                                        });
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"Error re-authenticating: {ex.Message}");
                                        _dispatcherQueue.TryEnqueue(() => HandleReconnectFailure());
                                    }
                                });
                            }
                            else
                            {
                                HandleReconnectFailure();
                            }
                        });

                        _isReconnecting = false;
                        break; 
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("Server still down, retrying...");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Reconnect loop error: {ex.Message}");
                }
            }
        }
        private void HandleReconnectFailure()
        {
            if (ConnectionStatusTextBlock != null)
            {
                ConnectionStatusTextBlock.Text = "Reconnection failed - please log in again";
                ConnectionStatusTextBlock.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 244, 67, 54));
            }
            LoginPanel.Visibility = Visibility.Visible;
            ChatPanel.Visibility = Visibility.Collapsed;
            _isConnected = false;
            UpdateWindowTitle();
            UpdateLogoBasedOnConnectionState();
        }
        private void ScrollToBottom()
        {
            _dispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    if (MessagesListView.Items.Count > 0)
                    {
                        MessagesListView.UpdateLayout();
                        var lastItem = MessagesListView.Items.Last();
                        MessagesListView.ScrollIntoView(lastItem, ScrollIntoViewAlignment.Leading);
                        await Task.Delay(50);
                        MessagesListView.ScrollIntoView(lastItem, ScrollIntoViewAlignment.Leading);
                        _isScrolledToBottom = true;
                        _hasNewMessages = false;
                        UpdateGoToNewButtonVisibility();
                    }
                }
                catch { }
            });
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (SettingsFrame != null)
                {
                    SettingsFrame.Navigate(typeof(SettingsPage), _currentUsername);
                }

                if (SettingsOverlay != null)
                {
                    SettingsOverlay.Visibility = Visibility.Visible;
                }
            }
            catch { }
        }

        private void SettingsPopup_Save(object sender, RoutedEventArgs e)
        {
            if (SettingsFrame.Content is SettingsPage settingsPage) settingsPage.SaveAllSettings();
            if (SettingsOverlay != null) SettingsOverlay.Visibility = Visibility.Collapsed;
        }

        private void SettingsPopup_Cancel(object sender, RoutedEventArgs e)
        {
            if (SettingsOverlay != null) SettingsOverlay.Visibility = Visibility.Collapsed;
        }


        private async void LogoButton_Click(object sender, RoutedEventArgs e)
        {
            await CancelRecordingIfActive();
            var globalChat = _allUsersCache.FirstOrDefault(u => u.Username == "Global Chat");
            if (globalChat != null)
            {
                UsersListView.SelectedItem = globalChat;
                _currentChatTarget = null;
                CurrentChatTitle.Text = "Global Chat";

                _messages.Clear();
                _filteredMessages.Clear();
                _searchQuery = "";
                if (UserSearchBox != null) UserSearchBox.Text = "";
                _ = _networkClient?.SendMessageAsync(new ProtocolMessage { Type = MessageType.GetHistory });
            }
        }

        private void ShowNotification(string title, string content, bool isPrivate)
        {
            try
            {
                var settings = uchat.Services.SettingsService.GetForUser(_currentUsername);
                if (settings.NotifyMode == uchat.Services.NotificationMode.None) return;

                if (settings.NotifyMode == uchat.Services.NotificationMode.Mentions)
                {
                    if (!isPrivate) 
                    {
                        string mentionTag = $"@{_currentUsername}";
                        bool isMentioned = content.IndexOf(mentionTag, StringComparison.OrdinalIgnoreCase) >= 0;

                        if (!isMentioned) return;
                    }
                }

                bool isLookingAtThisChat = false;

                if (_currentChatTarget == null && title == "Global Chat") isLookingAtThisChat = true;

                else if (_currentChatTarget == title) isLookingAtThisChat = true;

                if (isLookingAtThisChat && IsAppInForeground()) return;


                string displayContent = content;

                if (content.StartsWith("<STICKER:")) displayContent = "Sticker";
                else if (content.StartsWith("<GIF:")) displayContent = "GIF";
                else if (content.StartsWith("<VOICE:")) displayContent = "Voice Message";
                else if (content.StartsWith("<IMAGE:") || content.StartsWith("<IMG_REF:")) displayContent = "Photo";
                else if (content.StartsWith("<FILE:") || content.StartsWith("<FILE_REF:")) displayContent = "File";
                else if (content.StartsWith("<REPLY:"))
                {
                    int end = content.IndexOf('>');
                    if (end > 0 && end + 1 < content.Length)
                    {
                        displayContent = content.Substring(end + 1);
                        if (displayContent.StartsWith("<STICKER:")) displayContent = "Sticker (Reply)";
                        else if (displayContent.StartsWith("<GIF:")) displayContent = "GIF (Reply)";
                    }
                    else
                    {
                        displayContent = "Message (Reply)";
                    }
                }

                displayContent = displayContent.Replace("<", "&lt;").Replace(">", "&gt;");
                if (displayContent.Length > 100) displayContent = displayContent.Substring(0, 97) + "...";

                new CommunityToolkit.WinUI.Notifications.ToastContentBuilder()
                    .AddText(title)
                    .AddText(displayContent)
                    .Show();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Notification error: {ex.Message}");
            }
        }
        private string GetContentPreview(string rawContent)
        {
            if (string.IsNullOrEmpty(rawContent)) return "";
            if (rawContent.StartsWith("<VOICE:")) return "🎤 Voice Message";
            if (rawContent.StartsWith("<STICKER:")) return "🧩 Sticker";
            if (rawContent.StartsWith("<GIF:")) return "GIF";
            if (rawContent.StartsWith("<IMG_REF:") || rawContent.StartsWith("<IMAGE:")) return "📷 Image";
            if (rawContent.StartsWith("<FILE:") || rawContent.StartsWith("<FILE_REF:")) return "📄 File";
            return rawContent;
        }
        private string FormatPreviewText(string senderName, bool isOwn, string rawContent, bool isPrivateChat)
        {
            string contentType = GetContentPreview(rawContent);
            if (isOwn)
            {
                return $"You: {contentType}";
            }
            if (isPrivateChat)
            {
                return contentType;
            }
            else
            {
                return $"{senderName}: {contentType}";
            }
        }

        private void MarkAllReadButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var user in _allUsersCache) user.UnreadCount = 0;
            RefreshUserListUI();
        }

        private void LeftSplitter_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border border && ChatPanel != null)
            {
                _isResizingLeft = true;
                border.CapturePointer(e.Pointer);
                var point = e.GetCurrentPoint(ChatPanel);
                _initialMouseX = point.Position.X;
                if (ChatPanel.ColumnDefinitions.Count > 2)
                {
                    _initialColumnWidth = ChatPanel.ColumnDefinitions[2].ActualWidth;
                }
            }
        }
        private async void VoiceButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_voiceManager.IsRecording)
                {
                    await _voiceManager.StartRecordingAsync();
                    VoiceIcon.Glyph = "\uE71A";
                    VoiceButton.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 244, 67, 54));
                    if (CancelVoiceButton != null) CancelVoiceButton.Visibility = Visibility.Visible;
                }
                else
                {
                    var voiceBase64 = await _voiceManager.StopRecordingAsync();
                    VoiceIcon.Glyph = "\uE720";
                    VoiceButton.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
                    if (CancelVoiceButton != null) CancelVoiceButton.Visibility = Visibility.Collapsed;
                    if (!string.IsNullOrEmpty(voiceBase64) && _networkClient != null && _networkClient.IsConnected)
                    {
                                int? replyId = _replyingToMessage?.Id;
                                string? replySender = _replyingToMessage?.SenderUsername;
                                string? replyContent = _replyingToMessage?.Content;

                                if (_replyingToMessage != null)
                                {
                                    if (_replyingToMessage.IsImage) replyContent = "📷 Photo";
                                    else if (_replyingToMessage.IsFile) replyContent = "📄 File";
                                    else if (_replyingToMessage.IsVoice) replyContent = "🎤 Voice Message";
                                    else if (_replyingToMessage.IsSticker) replyContent = "🧩 Sticker";
                                    else if (_replyingToMessage.IsGif) replyContent = "GIF";
                                }

                                CancelReply_Click(null, null);

                        var localMsg = new MessageViewModel
                        {
                            Id = new Random().Next(int.MinValue, 0),
                            SenderUsername = _currentUsername,
                            SentAt = DateTime.UtcNow,
                            IsOwnMessage = true,
                            MessageStatus = "sent",
                            ChatTarget = _currentChatTarget,
                            Content = $"<VOICE:{voiceBase64}>",
                            Status = MessageStatusEnum.Sent,
                            ReplyToId = replyId,
                            ReplyToSender = replySender,
                            ReplyToContent = replyContent
                        };

                        _messages.Add(localMsg);
                        if (string.IsNullOrEmpty(_searchQuery)) _filteredMessages.Add(localMsg);

                        _dispatcherQueue.TryEnqueue(async () =>
                        {
                            await Task.Delay(50);
                            ScrollToBottom();
                        });

                        var parameters = new Dictionary<string, string>();
                                if (replyId.HasValue) parameters.Add("replyToId", replyId.Value.ToString());

                                var activeChat = _allUsersCache.FirstOrDefault(u => u.Username == _currentChatTarget);
                
                        if (activeChat != null && activeChat.IsGroup)
                        {
                            parameters.Add("groupId", activeChat.GroupId.ToString());
                            await _networkClient.SendMessageAsync(new ProtocolMessage
                            {
                                Type = MessageType.SendGroupMessage,
                                Data = $"<VOICE:{voiceBase64}>",
                                Parameters = parameters
                            });
                            UpdateLastMessage(activeChat.Username, "🎤 Voice message", DateTime.UtcNow);
                        }
                        else
                        {
                            if (_currentChatTarget != null) parameters.Add("targetUsername", _currentChatTarget);

                            await _networkClient.SendMessageAsync(new ProtocolMessage
                            {
                                Type = MessageType.SendVoiceMessage,
                                Data = voiceBase64,
                                Parameters = parameters
                            });

                            if (_currentChatTarget != null) UpdateLastMessage(_currentChatTarget, "🎤 Voice message", DateTime.UtcNow);
                            else UpdateLastMessage("Global Chat", "🎤 Voice message", DateTime.UtcNow);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (CancelVoiceButton != null) CancelVoiceButton.Visibility = Visibility.Collapsed;
                var dialog = new ContentDialog { Title = "Error", Content = $"Microphone error: {ex.Message}", PrimaryButtonText = "OK", XamlRoot = this.XamlRoot };
                await dialog.ShowAsync();
            }
        }

        private async void AttachButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker();
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);
                int? replyId = _replyingToMessage?.Id;
                string? replySender = _replyingToMessage?.SenderUsername;
                string? replyContent = _replyingToMessage?.Content;

                if (_replyingToMessage != null)
                {
                    if (_replyingToMessage.IsImage) replyContent = "📷 Photo";
                    else if (_replyingToMessage.IsFile) replyContent = "📄 File";
                    else if (_replyingToMessage.IsVoice) replyContent = "🎤 Voice Message";
                    else if (_replyingToMessage.IsSticker) replyContent = "🧩 Sticker";
                    else if (_replyingToMessage.IsGif) replyContent = "GIF";
                }

                CancelReply_Click(null, null); 
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                picker.ViewMode = PickerViewMode.Thumbnail;
                picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
                picker.FileTypeFilter.Add("*");
                var file = await picker.PickSingleFileAsync();
                if (file == null) return;
                var props = await file.GetBasicPropertiesAsync();
                if (props.Size > 15 * 1024 * 1024)
                {
                    var dialog = new ContentDialog
                    {
                        Title = "File is too big",
                        Content = "Please, choose other file that is < 15 Mb.",
                        PrimaryButtonText = "OK",
                        XamlRoot = this.XamlRoot
                    };
                    await dialog.ShowAsync();
                    return;
                }
                var fileName = file.Name;
                var ext = System.IO.Path.GetExtension(fileName).ToLower();
                bool isImage = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".jfif", ".webp" }.Contains(ext);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        string base64Data = "";
                        using (var stream = await file.OpenReadAsync())
                        {
                            using (var reader = new DataReader(stream.GetInputStreamAt(0)))
                            {
                                var bytes = new byte[stream.Size];
                                await reader.LoadAsync((uint)stream.Size);
                                reader.ReadBytes(bytes);
                                base64Data = Convert.ToBase64String(bytes);
                            }
                        }
                        _dispatcherQueue.TryEnqueue(async () =>
                        {
                            if (isImage)
                            {
                                var bitmap = await Base64ToBitmap(base64Data);

                                var localMsg = new MessageViewModel
                                {
                                    Id = new Random().Next(int.MinValue, 0),
                                    SenderUsername = _currentUsername,
                                    SentAt = DateTime.UtcNow,
                                    IsOwnMessage = true,
                                    ChatTarget = _currentChatTarget,
                                    IsImage = true,
                                    DisplayImage = bitmap,
                                    ImageData = base64Data,
                                    FileName = fileName,
                                    Content = $"<IMAGE:{base64Data}>",
                                    ReplyToId = replyId,
                                    ReplyToSender = replySender,
                                    ReplyToContent = replyContent
                                };

                                _messages.Add(localMsg);
                                if (string.IsNullOrEmpty(_searchQuery)) _filteredMessages.Add(localMsg);
                                ScrollToBottom();
                                try
                                {
                                    var msg = new ProtocolMessage
                                    {
                                        Type = isImage ? MessageType.SendImage : MessageType.SendFile,
                                        Data = base64Data,
                                        Parameters = new Dictionary<string, string> { { "filename", fileName } }
                                    };
                                    if (replyId.HasValue) msg.Parameters.Add("replyToId", replyId.Value.ToString());
                                    var activeChat = _allUsersCache.FirstOrDefault(u => u.Username == _currentChatTarget);

                                    if (activeChat != null && activeChat.IsGroup)
                                    {
                                        msg.Parameters.Add("groupId", activeChat.GroupId.ToString());
                                    }
                                    else if (_currentChatTarget != null)
                                    {
                                        msg.Parameters.Add("targetUsername", _currentChatTarget);
                                    }
                                    string mediaPreview = isImage ? "📷 Image" : $"📄 {fileName}";
                                    if (_currentChatTarget != null)
                                    {                                  
                                        UpdateLastMessage(_currentChatTarget, $"You: {mediaPreview}", DateTime.UtcNow);
                                    }
                                    else
                                    {
                                        UpdateLastMessage("Global Chat", $"You: {mediaPreview}", DateTime.UtcNow);
                                    }
                                    await _networkClient.SendMessageAsync(msg);
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine("Network send failed: " + ex.Message);
                                }
                            }
                            else
                            {
                                await SendFileMessageBackground(fileName, base64Data, replyId, replySender, replyContent);
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("Error sending file: " + ex.Message);
                    }
                });
            }
            catch { }
        }

        private string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{(bytes / 1024.0):F1} KB";
            return $"{(bytes / (1024.0 * 1024.0)):F2} MB";
        }

        private async Task SendFileMessageBackground(string fileName, string base64Data, int? replyId, string? rSender, string? rContent)
        {
            if (_networkClient == null || !_networkClient.IsConnected) return;          
          
           
            var localMsg = new MessageViewModel
            {
                Id = new Random().Next(int.MinValue, 0),
                SenderUsername = _currentUsername,
                SentAt = DateTime.UtcNow,
                IsOwnMessage = true,
                IsSystemMessage = false,
                MessageStatus = "sent",
                Status = MessageStatusEnum.Sent,
                ChatTarget = _currentChatTarget,
                Content = $"<FILE:{fileName}|{base64Data}>",
                FileSizeDisplay = FormatFileSize((long)(base64Data.Length * 3.0 / 4.0)),
                ReplyToId = replyId,
                ReplyToSender = rSender,
                ReplyToContent = rContent
            };

            _messages.Add(localMsg);
            if (string.IsNullOrEmpty(_searchQuery)) _filteredMessages.Add(localMsg);
            ScrollToBottom();

            _ = Task.Run(async () =>
            {
                try
                {
                    var msg = new ProtocolMessage
                    {
                        Type = MessageType.SendFile,
                        Data = base64Data,
                        Parameters = new Dictionary<string, string>
                {
                    { "filename", fileName }
                }
                    };
                    if (replyId.HasValue) msg.Parameters.Add("replyToId", replyId.Value.ToString());
                    var activeChat = _dispatcherQueue.HasThreadAccess
                ? _allUsersCache.FirstOrDefault(u => u.Username == _currentChatTarget)
                : null;
                    if (activeChat == null && !string.IsNullOrEmpty(_currentChatTarget))
                    {
                        activeChat = _allUsersCache.FirstOrDefault(u => u.Username == _currentChatTarget);
                    }

                    if (activeChat != null && activeChat.IsGroup)
                    {
                        msg.Parameters.Add("groupId", activeChat.GroupId.ToString());
                    }
                    else if (_currentChatTarget != null)
                    {
                        msg.Parameters.Add("targetUsername", _currentChatTarget);
                    }
                    await _networkClient.SendMessageAsync(msg);
                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        string preview = $"📄 {fileName}";
                        if (_currentChatTarget != null)
                            UpdateLastMessage(_currentChatTarget, preview, DateTime.UtcNow);
                        else
                            UpdateLastMessage("Global Chat", preview, DateTime.UtcNow);
                    });
                }
                catch { }
            });
        }

        private async void DownloadFile_Click(object sender, RoutedEventArgs e)
        {
            MessageViewModel? msg = null;
            if (sender is Button btn && btn.Tag is MessageViewModel m1) msg = m1;
            else if (sender is MenuFlyoutItem item && item.Tag is MessageViewModel m2) msg = m2;
            if (msg == null) return;
            if (!string.IsNullOrEmpty(msg.FileData) || !string.IsNullOrEmpty(msg.ImageData))
            {
                string data = msg.IsImage ? msg.ImageData : msg.FileData;
                string name = msg.FileName;

                if (string.IsNullOrEmpty(name)) name = $"uchat_file_{DateTime.Now.Ticks}";
                if (msg.IsImage && !name.Contains(".")) name += ".png";
                bool savedAuto = await TryAutoSaveFileAsync(name, data);
                if (!savedAuto)
                {
                    await SaveFileToDisk(name, data, msg.IsImage);
                }
                return;
            }
            if (msg.BlobId.HasValue && _networkClient != null && _networkClient.IsConnected)
            {
                msg.IsUserDownloading = true;
                msg.FileSizeDisplay = "Downloading..."; 

                await _networkClient.SendMessageAsync(new ProtocolMessage
                {
                    Type = MessageType.GetFileContent,
                    Data = msg.BlobId.Value.ToString()
                });
            }
        }

        private async Task SaveFileToDisk(string fileName, string base64Data, bool isImage)
        {
            try
            {
                if (string.IsNullOrEmpty(base64Data)) return;

                var picker = new FileSavePicker();

                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.m_window);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                picker.SuggestedStartLocation = PickerLocationId.Downloads;

                string extension = System.IO.Path.GetExtension(fileName).ToLower();
                if (string.IsNullOrEmpty(extension))
                {
                    extension = isImage ? ".png" : ".dat";
                }
                var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".jfif", ".webp" };
                bool isRealImage = imageExtensions.Contains(extension);

                picker.FileTypeChoices.Add(isRealImage ? "Image" : "File", new List<string>() { extension });
                picker.SuggestedFileName = fileName;

                var file = await picker.PickSaveFileAsync();
                if (file != null)
                {
                    var bytes = Convert.FromBase64String(base64Data);
                    await Windows.Storage.FileIO.WriteBytesAsync(file, bytes);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Save error: {ex.Message}");
            }
        }
        private void LeftSplitter_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_isResizingLeft && ChatPanel != null)
            {
                var point = e.GetCurrentPoint(ChatPanel);
                var deltaX = point.Position.X - _initialMouseX;
                var newWidth = _initialColumnWidth - deltaX;

                if (ChatPanel.ColumnDefinitions.Count > 2)
                {
                    var minWidth = 94.4;
                    var maxWidth = 600.0; 
                    newWidth = Math.Max(minWidth, Math.Min(maxWidth, newWidth));
                    ChatPanel.ColumnDefinitions[2].Width = new GridLength(newWidth);
                }
            }
        }

        private void LeftSplitter_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border border)
            {
                _isResizingLeft = false;
                border.ReleasePointerCapture(e.Pointer);
                
                if (ChatPanel != null && ChatPanel.ColumnDefinitions.Count > 2)
                {
                    UpdateChatListLayout(ChatPanel.ColumnDefinitions[2].ActualWidth);
                }
            }
        }

        private void RightSplitter_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border border && ChatPanel != null)
            {
                _isResizingRight = true;
                border.CapturePointer(e.Pointer);
                var point = e.GetCurrentPoint(ChatPanel);
                _initialMouseX = point.Position.X;
                if (ChatPanel.ColumnDefinitions.Count > 2)
                {
                    _initialColumnWidth = ChatPanel.ColumnDefinitions[2].ActualWidth;
                }
            }
        }

        private void RightSplitter_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_isResizingRight && ChatPanel != null)
            {
                var point = e.GetCurrentPoint(ChatPanel);
                var deltaX = point.Position.X - _initialMouseX;
                var newWidth = _initialColumnWidth + deltaX;

                if (ChatPanel.ColumnDefinitions.Count > 2)
                {
                    var minWidth = 94.4; 
                    var maxWidth = 600.0; 
                    newWidth = Math.Max(minWidth, Math.Min(maxWidth, newWidth));
                    ChatPanel.ColumnDefinitions[2].Width = new GridLength(newWidth);
                }
            }
        }

        private void RightSplitter_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border border)
            {
                _isResizingRight = false;
                border.ReleasePointerCapture(e.Pointer);
                
                if (ChatPanel != null && ChatPanel.ColumnDefinitions.Count > 2)
                {
                    UpdateChatListLayout(ChatPanel.ColumnDefinitions[2].ActualWidth);
                }
            }
        }

        private void UpdateChatListLayout(double width)
        {
            try
            {
                var isCollapsed = width <= 120;

                if (MyUsernameText != null)
                {
                    MyUsernameText.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
                }
                if (ConnectionStatusTextBlock != null)
                {
                    ConnectionStatusTextBlock.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
                }

                if (UserSearchBox != null)
                {
                    UserSearchBox.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
                }
                if (SearchPanel != null)
                {
                    SearchPanel.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
                }
            }
            catch { }
        }

        private void Splitter_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border border)
            {
                border.Background = new SolidColorBrush(Color.FromArgb(30, 76, 175, 80));
            }
        }

        private void Splitter_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border border && !_isResizingLeft && !_isResizingRight)
            {
                border.Background = new SolidColorBrush(Colors.Transparent);
            }
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T t)
                {
                    yield return t;
                }
                foreach (var childOfChild in FindVisualChildren<T>(child))
                {
                    yield return childOfChild;
                }
            }
        }
    }
    public class TextVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string text && text.StartsWith("<VOICE:") && text.EndsWith(">"))
                return Visibility.Collapsed;
            return Visibility.Visible;
        }
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public class VoiceVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string text && text.StartsWith("<VOICE:") && text.EndsWith(">"))
                return Visibility.Visible;
            return Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }
    public class ScheduledMessageViewModel
    {
        public int Id { get; set; }
        public string Content { get; set; } = string.Empty;
        public DateTime ScheduledAt { get; set; }
        public string ScheduledAtDisplay => $"Scheduled for: {ScheduledAt:yyyy-MM-dd HH:mm}";
        public string SenderUsername { get; set; } = string.Empty;
        public string? TargetUsername { get; set; }
        public bool IsPrivate { get; set; }
    }

    public class UnreadCountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is int count && count > 0) return Visibility.Visible;
            return Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public class SelectedToBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool isSelected && isSelected) return Windows.UI.Color.FromArgb(255, 76, 175, 80);
            return Windows.UI.Color.FromArgb(255, 75, 83, 32);
        }
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool boolValue && boolValue) return Visibility.Visible;
            return Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool boolValue && !boolValue) return Visibility.Visible;
            return Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public enum MessageStatusEnum
    {
        Sending,
        Sent,
        Read
    }

    public class MessageViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private string _content = string.Empty;
        private bool _isVoice;
        private bool _isGif;
        private bool _isImage;
        private bool _isFile;
        private bool _isSticker;
        public bool IsSticker
        {
            get => _isSticker;
            set
            {
                if (_isSticker != value)
                {
                    _isSticker = value;
                    OnPropertyChanged();
                    UpdateAllVisibilities(); 
                }
            }
        }

        private Microsoft.UI.Xaml.Media.ImageSource? _stickerSource;
        public Microsoft.UI.Xaml.Media.ImageSource? StickerSource
        {
            get => _stickerSource;
            set
            {
                if (_stickerSource != value)
                {
                    _stickerSource = value;
                    OnPropertyChanged();
                }
            }
        }
        private string? _voiceData;
        private string? _gifData;
        private string _messageStatus = "sent";
        public bool IsRead { get; set; }
        private bool _isVoicePlaying = false;
        private double _voiceProgressValue = 0;
        private TimeSpan _voiceDuration = TimeSpan.Zero;
        private TimeSpan _voicePosition = TimeSpan.Zero;
        private double _voiceVolumeValue = 100;
        private bool _showVolumeSlider = false;
        private bool _isVoiceMuted = false;
        public bool IsVoiceMuted
        {
            get => _isVoiceMuted;
            set { if (_isVoiceMuted != value) { _isVoiceMuted = value; OnPropertyChanged(); OnPropertyChanged(nameof(VoiceVolumeIconGlyph)); } }
        }
        private DateTime? _editedAt;
        private string? _imageData;
        private string? _fileData;
        private string? _fileName;
        private string? _fileSizeDisplay;
        private Microsoft.UI.Xaml.Media.ImageSource? _displayImage;
        public Visibility ShowImageLoading => (IsImage && !IsSticker && DisplayImage == null) ? Visibility.Visible : Visibility.Collapsed;
        public Microsoft.UI.Xaml.Media.ImageSource? DisplayImage
        {
            get => _displayImage;
            set
            {
                _displayImage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowImageLoading)); 
            }
        }
        public int Id { get; set; }
        public string SenderUsername { get; set; } = string.Empty;
        public string SenderLogin { get; set; } = string.Empty;
        public bool IsOwnMessage { get; set; }
        public bool IsSystemMessage { get; set; }
        public DateTime SentAt { get; set; }
        public string? ChatTarget { get; set; }
        public int? BlobId { get; set; }
        public Visibility ShowOpenPrivateChat =>
        (!IsSystemMessage && !IsOwnMessage && ChatTarget != SenderLogin)
        ? Visibility.Visible : Visibility.Collapsed;
        private int? _replyToId;
        public int? ReplyToId
        {
            get => _replyToId;
            set
            {
                if (_replyToId != value)
                {
                    _replyToId = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ShowReplyInfo));
                }
            }
        }
        private string? _replyToSender;
        public string? ReplyToSender
        {
            get => _replyToSender;
            set
            {
                if (_replyToSender != value)
                {
                    _replyToSender = value;
                    OnPropertyChanged();
                }
            }
        }
        private string? _replyToContent; 
        public string? ReplyToContent
        {
            get => _replyToContent;
            set
            {
                if (_replyToContent != value)
                {
                    _replyToContent = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ReplyPreviewText));
                }
            }
        }
        public string ReplyPreviewText
        {
            get
            {
                if (string.IsNullOrEmpty(ReplyToContent)) return "Deleted message";
                if (ReplyToContent.StartsWith("<IMAGE:") || ReplyToContent.StartsWith("<IMG_REF:")) return "📷 Photo";
                if (ReplyToContent.StartsWith("<VOICE:")) return "🎤 Voice message";
                if (ReplyToContent.StartsWith("<FILE:") || ReplyToContent.StartsWith("<FILE_REF:")) return "📄 File";
                if (ReplyToContent.StartsWith("<STICKER:")) return "🧩 Sticker";
                if (ReplyToContent.StartsWith("<GIF:")) return "GIF";

                return ReplyToContent.Replace("\n", " "); 
            }
        }
        public Visibility ShowReplyInfo => ReplyToId.HasValue ? Visibility.Visible : Visibility.Collapsed;

        public MessageViewModel Clone()
        {
            return new MessageViewModel
            {
                Id = this.Id,
                SenderUsername = this.SenderUsername,
                SenderLogin = this.SenderLogin,
                Content = this.Content,
                SentAt = this.SentAt,
                IsOwnMessage = this.IsOwnMessage,
                IsSystemMessage = this.IsSystemMessage,
                MessageStatus = this.MessageStatus,
                Status = this.Status,
                ChatTarget = this.ChatTarget,
                BlobId = this.BlobId,
                VoiceData = this.VoiceData,
                GifData = this.GifData,
                ImageData = this.ImageData,
                FileData = this.FileData,
                FileName = this.FileName,
                FileSizeDisplay = this.FileSizeDisplay,
                DisplayImage = this.DisplayImage,
                IsVoice = this.IsVoice,
                IsGif = this.IsGif,
                IsImage = this.IsImage,
                IsFile = this.IsFile
            };
        }

        public string Content
        {
            get => _content;
            set
            {
                if (_content != value)
                {
                    string rawInput = value ?? "";
                    _content = rawInput;
                    if (rawInput.StartsWith("<VOICE:") && rawInput.EndsWith(">"))
                    {
                        try
                        {
                            _voiceData = rawInput.Substring(7, rawInput.Length - 8);
                            bool wasVoice = _isVoice;
                            _isVoice = true;
                            _isGif = false;
                            _content = "🎤 Voice message";

                            if (!wasVoice)
                            {
                                _voiceVolumeValue = MainPage._defaultVoiceVolume;
                                OnPropertyChanged(nameof(VoiceVolumeValue));
                            }
                        }
                        catch
                        {
                            _content = "Audio error";
                            _isVoice = false;
                        }
                    }
                    else if (rawInput.StartsWith("<GIF:") && rawInput.EndsWith(">"))
                    {
                        _isGif = true;
                        _isVoice = false;
                    }
                    else if (rawInput.StartsWith("<IMAGE:") && rawInput.EndsWith(">"))
                    {
                        try
                        {
                            var contentData = rawInput.Substring(7, rawInput.Length - 8);
                            if (contentData.Contains("|"))
                            {
                                var parts = contentData.Split('|', 2);
                                _imageData = parts[0];
                                _fileName = parts[1];
                            }
                            else
                            {
                                _imageData = contentData;
                                _fileName = "image.png";
                            }
                            _isImage = true;
                            _isFile = false;
                        }
                        catch { }
                    }
                    else if (rawInput.StartsWith("<FILE:") && rawInput.EndsWith(">"))
                    {
                        try
                        {
                            var inner = rawInput.Substring(6, rawInput.Length - 7);
                            var parts = inner.Split('|', 2);
                            if (parts.Length == 2)
                            {
                                _fileName = parts[0];
                                _fileData = parts[1];
                                _isFile = true;
                                _fileSizeDisplay = CalculateFileSize(_fileData.Length);
                            }
                        }
                        catch { }
                    }
                    else if (rawInput.StartsWith("<FILE_REF:") && rawInput.EndsWith(">"))
                    {
                        try
                        {
                            var inner = rawInput.Substring(10, rawInput.Length - 11);
                            var parts = inner.Split('|');
                            if (parts.Length >= 2)
                            {
                                if (int.TryParse(parts[0], out int bId)) BlobId = bId;
                                _fileName = parts[1];
                                _isFile = true;
                                _isImage = false;
                                _fileData = null;
                                if (parts.Length >= 3 && long.TryParse(parts[2], out long sizeBytes))
                                {
                                    _fileSizeDisplay = $"{FormatFileSize(sizeBytes)} • Tap to download";
                                }
                                else
                                {
                                    _fileSizeDisplay = "Tap to download";
                                }
                            }
                        }
                        catch { _content = "Error parsing file"; }
                    }
                    else if (rawInput.StartsWith("<IMG_REF:") && rawInput.EndsWith(">"))
                    {
                        try
                        {
                            var inner = rawInput.Substring(9, rawInput.Length - 10);
                            var parts = inner.Split('|', 2);

                            if (parts.Length == 2)
                            {
                                if (int.TryParse(parts[0], out int bId)) BlobId = bId;
                                _fileName = parts[1];
                                _isImage = true;
                                _isFile = false;
                                _imageData = null;
                                _displayImage = null;
                            }
                        }
                        catch { }
                    }
                    else if (rawInput.StartsWith("<STICKER:") && rawInput.EndsWith(">"))
                    {
                        try
                        {
                            var inner = rawInput.Substring(9, rawInput.Length - 10);
                            var parts = inner.Split('|');

                            if (parts.Length == 2)
                            {
                                _fileName = parts[1];
                                _isImage = true;
                                _isFile = false;
                                _isVoice = false;
                                _isGif = false;
                            }
                        }
                        catch { }
                    }
                    else
                    {
                        _isVoice = false;
                        _isGif = false;
                    }

                    OnPropertyChanged(nameof(Content));
                    UpdateAllVisibilities();
                }
            }
        }

        private MessageStatusEnum _status;
        public MessageStatusEnum Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusGlyph)); 
                    OnPropertyChanged(nameof(StatusColor)); 
                }
            }
        }
        public string StatusGlyph => Status switch
        {
            MessageStatusEnum.Sending => "\uE916", 
            MessageStatusEnum.Sent => "\uE73E",   
            MessageStatusEnum.Read => "\uE73E\uE73E",    
            _ => ""
        };

        public Microsoft.UI.Xaml.Media.Brush StatusColor => Status == MessageStatusEnum.Read
            ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 76, 175, 80)) 
            : new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 158, 158, 158)); 

        private string CalculateFileSize(int base64Length)
        {
            double sizeInBytes = (base64Length * 3) / 4;

            if (sizeInBytes < 1024)
                return $"{sizeInBytes} B";
            else if (sizeInBytes < 1024 * 1024)
                return $"{(sizeInBytes / 1024.0):F1} KB";
            else
                return $"{(sizeInBytes / (1024.0 * 1024.0)):F2} MB";
        }
        public bool IsUserDownloading { get; set; } = false;
        private string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{(bytes / 1024.0):F1} KB";
            return $"{(bytes / (1024.0 * 1024.0)):F2} MB";
        }

        public string? VoiceData
        {
            get => _voiceData;
            set { if (_voiceData != value) { _voiceData = value; OnPropertyChanged(); } }
        }
        public string? GifData
        {
            get => _gifData;
            set { if (_gifData != value) { _gifData = value; OnPropertyChanged(); } }
        }

        public string? ImageData { get => _imageData; set { _imageData = value; OnPropertyChanged(); } }
        public string? FileData { get => _fileData; set { _fileData = value; OnPropertyChanged(); } }
        public string? FileName { get => _fileName; set { _fileName = value; OnPropertyChanged(); } }
        public string? FileSizeDisplay { get => _fileSizeDisplay; set { _fileSizeDisplay = value; OnPropertyChanged(); } }

        public bool IsFile { get => _isFile; set { _isFile = value; OnPropertyChanged(); UpdateAllVisibilities(); } }
        public bool IsImage { get => _isImage; set { _isImage = value; OnPropertyChanged(); UpdateAllVisibilities(); } }
        public bool IsVoice
        {
            get => _isVoice;
            set { if (_isVoice != value) { _isVoice = value; OnPropertyChanged(); UpdateAllVisibilities(); } }
        }
        public bool IsGif
        {
            get => _isGif;
            set { if (_isGif != value) { _isGif = value; OnPropertyChanged(); UpdateAllVisibilities(); } }
        }

        private void UpdateAllVisibilities()
        {
            OnPropertyChanged(nameof(ShowSticker));
            OnPropertyChanged(nameof(ShowText));
            OnPropertyChanged(nameof(ShowVoicePlayer));
            OnPropertyChanged(nameof(ShowGif));
            OnPropertyChanged(nameof(ShowImage));
            OnPropertyChanged(nameof(ShowFile));
            OnPropertyChanged(nameof(ShowProfileOption));
            OnPropertyChanged(nameof(ShowEditOption));
            OnPropertyChanged(nameof(ShowCopyOption));
            OnPropertyChanged(nameof(ShowSaveAsOption));
        }

        public Visibility ShowSticker => IsSticker ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ShowText => (!_isVoice && !_isGif && !_isFile && !_isImage && !_isSticker) ? Visibility.Visible : Visibility.Collapsed;

        public Visibility ShowVoicePlayer => _isVoice ? Visibility.Visible : Visibility.Collapsed;

        public Visibility ShowGif => _isGif ? Visibility.Visible : Visibility.Collapsed;

        public Visibility ShowFile => _isFile ? Visibility.Visible : Visibility.Collapsed;

        public Visibility ShowImage => _isImage ? Visibility.Visible : Visibility.Collapsed;
        
        public string VoicePlayButtonGlyph => _isVoicePlaying ? "\uE769" : "\uE768";
        public double VoiceProgressValue
        {
            get => _voiceProgressValue;
            set { if (Math.Abs(_voiceProgressValue - value) > 0.01) { _voiceProgressValue = value; OnPropertyChanged(); } }
        }
        public string VoiceTimeDisplay
        {
            get
            {
                if (_voiceDuration.TotalSeconds == 0) return "0:00 / 0:00";
                string position = FormatTimeSpan(_voicePosition);
                string duration = FormatTimeSpan(_voiceDuration);
                return $"{position} / {duration}";
            }
        }
        public string VoiceVolumeIconGlyph
        {
            get
            {
                if (_isVoiceMuted || _voiceVolumeValue == 0) return "\uE74F"; 
                return "\uE767";
            }
        }
        public double VoiceVolumeValue
        {
            get => _voiceVolumeValue;
            set { if (Math.Abs(_voiceVolumeValue - value) > 0.01) { _voiceVolumeValue = value; OnPropertyChanged(); OnPropertyChanged(nameof(VoiceVolumeIconGlyph)); } }
        }
        public bool ShowVolumeSlider
        {
            get => _showVolumeSlider;
            set { if (_showVolumeSlider != value) { _showVolumeSlider = value; OnPropertyChanged(); } }
        }
        public bool IsVoicePlaying
        {
            get => _isVoicePlaying;
            set { if (_isVoicePlaying != value) { _isVoicePlaying = value; OnPropertyChanged(); OnPropertyChanged(nameof(VoicePlayButtonGlyph)); } }
        }
        public TimeSpan VoiceDuration
        {
            get => _voiceDuration;
            set { if (_voiceDuration != value) { _voiceDuration = value; OnPropertyChanged(); OnPropertyChanged(nameof(VoiceTimeDisplay)); } }
        }
        public TimeSpan VoicePosition
        {
            get => _voicePosition;
            set
            {
                if (Math.Abs((_voicePosition - value).TotalSeconds) > 0.1)
                {
                    _voicePosition = value;
                    if (_voiceDuration.TotalSeconds > 0)
                    {
                        VoiceProgressValue = (_voicePosition.TotalSeconds / _voiceDuration.TotalSeconds) * 100;
                    }
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(VoiceTimeDisplay));
                }
            }
        }
        
        private string FormatTimeSpan(TimeSpan span)
        {
            if (span.TotalHours >= 1)
                return $"{(int)span.TotalHours}:{span.Minutes:D2}:{span.Seconds:D2}";
            else
                return $"{span.Minutes}:{span.Seconds:D2}";
        }
        

        public Visibility ShowSaveAsOption => ((IsFile || IsImage) && !IsSticker) ? Visibility.Visible : Visibility.Collapsed;

        public Visibility ShowCopyOption => (!_isVoice && !_isGif && !_isFile && !_isImage && !_isSticker) ? Visibility.Visible : Visibility.Collapsed;

        public Visibility ShowEditOption => (IsOwnMessage && !IsSystemMessage && !_isVoice && !_isGif && !_isFile && !_isImage && !_isSticker) ? Visibility.Visible : Visibility.Collapsed;

        public Visibility ShowProfileOption => (!IsOwnMessage && !IsSystemMessage) ? Visibility.Visible : Visibility.Collapsed;

        public Visibility ShowDeleteOption => (IsOwnMessage && !IsSystemMessage)
            ? Visibility.Visible : Visibility.Collapsed;
        public string MessageStatus
        {
            get => _messageStatus;
            set
            {
                if (_messageStatus != value)
                {
                    _messageStatus = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(StatusColor));
                }
            }
        }

        public DateTime? EditedAt
        {
            get => _editedAt;
            set { if (_editedAt != value) { _editedAt = value; OnPropertyChanged(); OnPropertyChanged(nameof(TimeDisplay)); OnPropertyChanged(nameof(EditedTimeDisplay)); OnPropertyChanged(nameof(ShowEditedTime)); } }
        }

        public string TimeDisplay => EditedAt.HasValue ? $"{SentAt.ToLocalTime():HH:mm} (edited)" : SentAt.ToLocalTime().ToString("HH:mm");
        
        public string EditedTimeDisplay
        {
            get
            {
                if (!EditedAt.HasValue) return string.Empty;
                var editedTime = EditedAt.Value.ToLocalTime();
                var now = DateTime.Now;
                if (editedTime.Date == now.Date)
                {
                    return $"Edited at {editedTime:HH:mm}";
                }
                else if (editedTime.Date == now.Date.AddDays(-1))
                {
                    return $"Edited yesterday at {editedTime:HH:mm}";
                }
                else
                {
                    return $"Edited {editedTime:MMM d, yyyy 'at' HH:mm}";
                }
            }
        }
        
        public Visibility ShowEditedTime => EditedAt.HasValue ? Visibility.Visible : Visibility.Collapsed;

        public Microsoft.UI.Xaml.Media.Brush BackgroundBrush
        {
            get
            {
                if (IsSystemMessage) return new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(180, 66, 45, 100));
                if (IsOwnMessage) return new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 30, 47, 32));
                return new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 36, 36, 36));
            }
        }

        public HorizontalAlignment HorizontalAlignment => IsSystemMessage ? HorizontalAlignment.Center : (IsOwnMessage ? HorizontalAlignment.Right : HorizontalAlignment.Left);
        public Visibility ShowSenderName => (IsSystemMessage || IsOwnMessage) ? Visibility.Collapsed : Visibility.Visible;
        public Visibility ShowEditDelete => IsOwnMessage ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ShowTimestamp => IsSystemMessage ? Visibility.Collapsed : Visibility.Visible;
        public Visibility ShowStatus => IsSystemMessage ? Visibility.Collapsed : (IsOwnMessage ? Visibility.Visible : Visibility.Collapsed);

        public string StatusText => MessageStatus == "read" ? "Read" : "Sent";
        public void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    public class NullToVisibilityConverter : Microsoft.UI.Xaml.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool isNull = value == null;
            if (parameter is string p && p == "Inverse")
                return isNull ? Visibility.Visible : Visibility.Collapsed;
            return isNull ? Visibility.Collapsed : Visibility.Visible;
        }
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public class GlobalChatVisibilityConverter : Microsoft.UI.Xaml.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string username && username == "Global Chat")
                return Visibility.Visible;
            return Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }
    public class MessageBackgroundConverter : Microsoft.UI.Xaml.Data.IValueConverter
    {
        public Microsoft.UI.Xaml.Media.SolidColorBrush OwnMessageBrush { get; set; } = new SolidColorBrush(Colors.Transparent);
        public Microsoft.UI.Xaml.Media.SolidColorBrush OtherMessageBrush { get; set; } = new SolidColorBrush(Colors.Transparent);

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            try
            {
                if (OwnMessageBrush == null) OwnMessageBrush = new SolidColorBrush(Colors.Transparent);
                if (OtherMessageBrush == null) OtherMessageBrush = new SolidColorBrush(Colors.Transparent);

                if (value is bool isOwn && isOwn) return OwnMessageBrush;
                return OtherMessageBrush;
            }
            catch { return new SolidColorBrush(Colors.Transparent); }
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public class NotGlobalChatVisibilityConverter : Microsoft.UI.Xaml.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string username && username == "Global Chat")
                return Visibility.Collapsed;
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }

    public class GifPickerItem : INotifyPropertyChanged
    {
        private Microsoft.UI.Xaml.Media.ImageSource? _image;

        public string Filename { get; set; } = "";

        public Microsoft.UI.Xaml.Media.ImageSource? Image
        {
            get => _image;
            set
            {
                _image = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Image)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

}
