using System;
using System.Threading.Tasks;

namespace TripFund.App.Services
{
    public interface INativeDatePickerService
    {
        Task<DateTime?> ShowDatePickerAsync(DateTime initialDate);
        Task<TimeSpan?> ShowTimePickerAsync(DateTime initialTime);
    }
}
