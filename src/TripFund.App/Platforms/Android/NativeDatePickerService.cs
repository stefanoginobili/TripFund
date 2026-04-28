using Android.App;
using Android.Content;
using Android.Views;
using Microsoft.Maui.ApplicationModel;
using System;
using System.Threading.Tasks;
using TripFund.App.Services;

namespace TripFund.App.Platforms.Android
{
    public class NativeDatePickerService : INativeDatePickerService
    {
        public Task<DateTime?> ShowDatePickerAsync(DateTime initialDate, DateTime? minDate = null)
        {
            var tcs = new TaskCompletionSource<DateTime?>();
            var context = Platform.CurrentActivity;

            if (context == null)
            {
                tcs.SetResult(null);
                return tcs.Task;
            }

            var dialog = new DatePickerDialog(context, (s, e) =>
            {
                if (!tcs.Task.IsCompleted) tcs.TrySetResult(new DateTime(e.Year, e.Month + 1, e.DayOfMonth));
            }, initialDate.Year, initialDate.Month - 1, initialDate.Day);

            if (minDate.HasValue)
            {
                dialog.DatePicker.MinDate = new DateTimeOffset(minDate.Value).ToUnixTimeMilliseconds();
            }

            dialog.SetCancelable(true);
            dialog.SetCanceledOnTouchOutside(true);
            dialog.CancelEvent += (s, e) => { if (!tcs.Task.IsCompleted) tcs.TrySetResult(null); };
            
            // Set the Cancel button text to "Annulla" and ensure it's visible
            dialog.SetButton((int)DialogButtonType.Negative, "Annulla", (s, e) => 
            {
                if (!tcs.Task.IsCompleted) tcs.TrySetResult(null);
            });

            dialog.Show();

            return tcs.Task;
        }

        public Task<TimeSpan?> ShowTimePickerAsync(DateTime initialTime)
        {
            var tcs = new TaskCompletionSource<TimeSpan?>();
            var context = Platform.CurrentActivity;

            if (context == null)
            {
                tcs.SetResult(null);
                return tcs.Task;
            }

            var dialog = new TimePickerDialog(context, (s, e) =>
            {
                if (!tcs.Task.IsCompleted) tcs.TrySetResult(new TimeSpan(e.HourOfDay, e.Minute, 0));
            }, initialTime.Hour, initialTime.Minute, true);

            dialog.SetCancelable(true);
            dialog.SetCanceledOnTouchOutside(true);
            dialog.CancelEvent += (s, e) => { if (!tcs.Task.IsCompleted) tcs.TrySetResult(null); };
            
            // Set the Cancel button text to "Annulla" and ensure it's visible
            dialog.SetButton((int)DialogButtonType.Negative, "Annulla", (s, e) => 
            {
                if (!tcs.Task.IsCompleted) tcs.TrySetResult(null);
            });

            dialog.Show();

            return tcs.Task;
        }
    }
}
