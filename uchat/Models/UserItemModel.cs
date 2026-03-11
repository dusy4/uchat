using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media;

namespace uchat.Models
{
    public class UserItemModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
             PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        public int Id { get; set; }

        private string _username = string.Empty;
        public string Username
        {
            get => _username;
            set
            {
                if (_username != value)
                {
                    _username = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Initials));
                }
            }
        }

        private string _displayName = string.Empty;
        public string DisplayName
        {
            get => string.IsNullOrEmpty(_displayName) ? Username : _displayName;
            set { _displayName = value; OnPropertyChanged(); OnPropertyChanged(nameof(Initials)); }
        }
        public string Initials => string.IsNullOrEmpty(Username) ? "?" : Username.Substring(0, 1).ToUpper();
        public string AvatarColor { get; set; } = "#0088CC";
        public bool IsGroup { get; set; } 
        public int GroupId { get; set; }
        public int CreatorId { get; set; } 
        public bool IsGroupOwner { get; set; }
        public string ExitMenuText
        {
            get
            {
                if (!IsGroup) return "Delete Chat";
                return IsGroupOwner ? "Delete Group" : "Leave Group";
            }
        }
        public void RefreshMenuText()
        {
            OnPropertyChanged(nameof(ExitMenuText));
        }
        public string? AvatarData { get; set; } 
        private ImageSource? _profileImage;
        public ImageSource? ProfileImage
        {
            get => _profileImage;
            set
            {
                _profileImage = value;
                OnPropertyChanged(); 
            }
        }
        private string _lastMessage = string.Empty;
        public string LastMessage
        {
            get => _lastMessage;
            set
            {
                if (_lastMessage != value)
                {
                    _lastMessage = value;
                    OnPropertyChanged(nameof(LastMessage)); 
                }
            }
        }
        private DateTime _lastMessageTime;
        public DateTime LastMessageTime
        {
            get => _lastMessageTime;
            set
            {
                if (_lastMessageTime != value)
                {
                    _lastMessageTime = value;
                }
            }
        }
        private int _unreadCount = 0;
        public int UnreadCount
        {
            get => _unreadCount;
            set
            {
                if (_unreadCount != value)
                {
                    _unreadCount = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public string Bio { get; set; } = string.Empty;
        public DateTime? CreatedAt { get; set; }
        public bool IsDeleted { get; set; }
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

    }
}