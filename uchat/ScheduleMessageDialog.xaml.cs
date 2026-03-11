using Microsoft.UI.Xaml.Controls;
using System;

namespace uchat
{
    public sealed partial class ScheduleMessageDialog : ContentDialog
    {
        public DateTime? ScheduledDateTime { get; private set; }

        public ScheduleMessageDialog(string messageContent, string? targetUsername)
        {
            this.InitializeComponent();
            MessageTextBox.Text = messageContent;
            
            var defaultTime = DateTime.Now.AddHours(1);
            DatePicker.Date = defaultTime;
            TimePicker.Time = defaultTime.TimeOfDay;
            
            this.PrimaryButtonClick += ContentDialog_PrimaryButtonClick;
        }

        private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            var deferral = args.GetDeferral();
            
            try
            {
                if (DatePicker.Date == null)
                {
                    args.Cancel = true;
                    return;
                }

                var date = DatePicker.Date.Value.Date;
                var time = TimePicker.Time;
                var scheduledDateTime = date.Add(time);

                if (scheduledDateTime <= DateTime.Now)
                {
                    args.Cancel = true;
                    return;
                }

                ScheduledDateTime = scheduledDateTime;
            }
            finally
            {
                deferral.Complete();
            }
        }
    }
}

