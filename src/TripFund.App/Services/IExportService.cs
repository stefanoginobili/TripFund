using TripFund.App.Models;

namespace TripFund.App.Services;

public interface IExportService
{
    Task<string> GenerateExportZipAsync(string tripSlug, TripConfig config, List<Transaction> transactions);
}
