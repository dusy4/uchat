using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using uchat.Protocol;
using uchat.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace uchat
{
    public sealed partial class PlannedMessagesDialog : ContentDialog
    {
        private readonly List<ScheduledMessageViewModel> _scheduledMessages;
        private readonly string _targetUsername;
        private readonly NetworkClient? _networkClient;

        public PlannedMessagesDialog(List<ScheduledMessageViewModel> scheduledMessages, string targetUsername, NetworkClient? networkClient)
        {
            this.InitializeComponent();
            _scheduledMessages = scheduledMessages;
            _targetUsername = targetUsername;
            _networkClient = networkClient;
            
            ScheduledMessagesList.ItemsSource = _scheduledMessages;
            ScheduledMessagesList.ContainerContentChanging += ScheduledMessagesList_ContainerContentChanging;
        }
        
        private void ScheduledMessagesList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue) return;
            
            if (args.ItemContainer?.ContentTemplateRoot is FrameworkElement root)
            {
                var buttons = FindVisualChildren<Button>(root).ToList();
                foreach (var button in buttons)
                {
                    if (button.Content?.ToString() == "Edit")
                    {
                        button.Click -= EditScheduledMessage_Click;
                        button.Click += EditScheduledMessage_Click;
                    }
                    else if (button.Content?.ToString() == "Delete")
                    {
                        button.Click -= DeleteScheduledMessage_Click;
                        button.Click += DeleteScheduledMessage_Click;
                    }
                }
            }
            else
            {
                args.RegisterUpdateCallback((ListViewBase s, ContainerContentChangingEventArgs a) =>
                {
                    if (a.ItemContainer?.ContentTemplateRoot is FrameworkElement r)
                    {
                        var btns = FindVisualChildren<Button>(r).ToList();
                        foreach (var btn in btns)
                        {
                            if (btn.Content?.ToString() == "Edit")
                            {
                                btn.Click -= EditScheduledMessage_Click;
                                btn.Click += EditScheduledMessage_Click;
                            }
                            else if (btn.Content?.ToString() == "Delete")
                            {
                                btn.Click -= DeleteScheduledMessage_Click;
                                btn.Click += DeleteScheduledMessage_Click;
                            }
                        }
                    }
                });
            }
        }
        
        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t)
                    yield return t;
                foreach (var childOfChild in FindVisualChildren<T>(child))
                    yield return childOfChild;
            }
        }

        private async void EditScheduledMessage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int messageId)
            {
                var message = _scheduledMessages.FirstOrDefault(m => m.Id == messageId);
                if (message == null) return;

                var editDialog = new ScheduleMessageDialog(message.Content, _targetUsername);
                editDialog.XamlRoot = this.XamlRoot;
                
                var localTime = message.ScheduledAt.ToLocalTime();
                var datePicker = editDialog.FindName("DatePicker") as CalendarDatePicker;
                var timePicker = editDialog.FindName("TimePicker") as TimePicker;
                var messageTextBox = editDialog.FindName("MessageTextBox") as TextBox;
                
                if (datePicker != null) datePicker.Date = localTime;
                if (timePicker != null) timePicker.Time = localTime.TimeOfDay;
                
                if (await editDialog.ShowAsync() == ContentDialogResult.Primary && editDialog.ScheduledDateTime.HasValue)
                {
                    var messageText = messageTextBox?.Text ?? message.Content;
                    await _networkClient?.SendMessageAsync(new ProtocolMessage
                    {
                        Type = MessageType.UpdateScheduledMessage,
                        Data = messageText,
                        Parameters = new Dictionary<string, string>
                        {
                            { "messageId", messageId.ToString() },
                            { "scheduledAt", editDialog.ScheduledDateTime.Value.ToUniversalTime().ToString("O") }
                        }
                    });
                    
                    // Update local list
                    message.Content = messageText;
                    message.ScheduledAt = editDialog.ScheduledDateTime.Value;
                    ScheduledMessagesList.ItemsSource = null;
                    ScheduledMessagesList.ItemsSource = _scheduledMessages;
                }
            }
        }

        private async void DeleteScheduledMessage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int messageId)
            {
                await _networkClient?.SendMessageAsync(new ProtocolMessage
                {
                    Type = MessageType.DeleteScheduledMessage,
                    Parameters = new Dictionary<string, string>
                    {
                        { "messageId", messageId.ToString() }
                    }
                });
                
                // Remove from local list
                var message = _scheduledMessages.FirstOrDefault(m => m.Id == messageId);
                if (message != null)
                {
                    _scheduledMessages.Remove(message);
                    ScheduledMessagesList.ItemsSource = null;
                    ScheduledMessagesList.ItemsSource = _scheduledMessages;
                }
            }
        }
    }
}

