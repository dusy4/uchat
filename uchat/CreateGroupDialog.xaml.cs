using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;
using System.Linq;
using uchat.Models;

namespace uchat
{
    public sealed partial class CreateGroupDialog : ContentDialog
    {
        public string GroupName => GroupNameBox.Text;
        public List<int> SelectedUserIds { get; private set; } = new();

        public CreateGroupDialog(List<UserItemModel> users)
        {
            this.InitializeComponent();
            MembersList.ItemsSource = users
                .Where(u => !u.IsGroup && u.Username != "Global Chat")
                .ToList();

            this.PrimaryButtonClick += ContentDialog_PrimaryButtonClick;
        }

        private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            if (string.IsNullOrWhiteSpace(GroupNameBox.Text))
            {
                args.Cancel = true; 
                GroupNameBox.Header = "Group Name (Required!)"; 
                return;
            }

            SelectedUserIds.Clear();
            if (MembersList.SelectedItems != null)
            {
                foreach (var item in MembersList.SelectedItems)
                {
                    if (item is UserItemModel user)
                    {
                        SelectedUserIds.Add(user.Id);
                    }
                }
            }
        }
    }
}