namespace TripFund.App.Services;

public interface IExchangeRateService
{
    /// <summary>
    /// Gets the exchange rate for a specific currency on a specific date (1 EUR = X Foreign Currency).
    /// Returns 1.0 for EUR.
    /// </summary>
    Task<decimal?> GetRateAsync(string tripSlug, string currency, DateTime date);

    /// <summary>
    /// Checks if any rates are missing from the local cache for the given transaction dates and currencies.
    /// </summary>
    Task<bool> AreRatesMissingAsync(string tripSlug, IEnumerable<DateTime> dates, IEnumerable<string> currencies);

    /// <summary>
    /// Downloads missing rates from the ECB API and saves them to the local cache.
    /// </summary>
    Task DownloadMissingRatesAsync(string tripSlug, IEnumerable<DateTime> dates, IEnumerable<string> currencies);
}
