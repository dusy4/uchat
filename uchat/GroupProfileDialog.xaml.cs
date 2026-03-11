using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using uchat.Models;
using Windows.Foundation;
using Windows.Foundation.Collections;


namespace uchat
{
    public sealed partial class GroupProfileDialog : ContentDialog
    {
        private readonly int _currentUserId;
        private int _creatorId;
        private int _groupId;
        public event System.Action<int, string>? AddMemberRequested;
        public event System.Action<int, int>? KickMemberRequested;
        public event System.Action<int>? DeleteGroupRequested;

        public GroupProfileDialog(int currentUserId)
        {
            this.InitializeComponent();
            _currentUserId = currentUserId;
        }
        public void UpdateData(int groupId, string groupName, int creatorId, System.Collections.Generic.List<UserItemModel> members)
        {
            _groupId = groupId;
            _creatorId = creatorId;

            GroupNameText.Text = groupName;
            MembersCountText.Text = $"{members.Count} members";
            bool isAdmin = _currentUserId == _creatorId;
            AdminPanel.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;
            foreach (var m in members)
            {
                m.IsSelected = isAdmin && (m.Id != _currentUserId);
            }

            MembersList.ItemsSource = members;
        }

        private void AddMember_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(NewMemberBox.Text))
            {
                AddMemberRequested?.Invoke(_groupId, NewMemberBox.Text);
                NewMemberBox.Text = "";
            }
        }

        private void KickMember_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int userId)
            {
                KickMemberRequested?.Invoke(_groupId, userId);
            }
        }
        private void DeleteGroup_Click(object sender, RoutedEventArgs e)
        {
            DeleteGroupRequested?.Invoke(_groupId);
            this.Hide(); 
        }
    }
}
