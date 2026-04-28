using System;
using System.Threading.Tasks;
using TripFund.App.Services;

namespace TripFund.App.Services
{
#if !ANDROID
    public class NativeDatePickerService : INativeDatePickerService
    {
        public Task<DateTime?> ShowDatePickerAsync(DateTime initialDate, DateTime? minDate = null)
        {
            // For non-Android platforms, we could implement native pickers or just return null
            // For now, return the initial date as we can't easily trigger a native picker from here without platform specific code
            return Task.FromResult<DateTime?>(initialDate);
        }

        public Task<TimeSpan?> ShowTimePickerAsync(DateTime initialTime)
        {
            return Task.FromResult<TimeSpan?>(initialTime.TimeOfDay);
        }
    }
#endif
}
