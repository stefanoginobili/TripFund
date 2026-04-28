using System;
using System.Threading.Tasks;

namespace TripFund.App.Services
{
    public interface INativeDatePickerService
    {
        Task<DateTime?> ShowDatePickerAsync(DateTime initialDate, DateTime? minDate = null);
        Task<TimeSpan?> ShowTimePickerAsync(DateTime initialTime);
    }
}
