using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using TripFund.App.Constants;

namespace TripFund.App.Services;

public class ExchangeRateService : IExchangeRateService
{
    private readonly HttpClient _httpClient;
    private readonly string _tripsPath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    // Cache in memory to avoid constant disk I/O during a single session/operation
    private readonly Dictionary<string, MonthlyRatesCache> _memoryCache = new();
    private readonly SemaphoreSlim _downloadSemaphore = new(1, 1);

    public ExchangeRateService(HttpClient httpClient, string? rootPath = null)
    {
        _httpClient = httpClient;
        _tripsPath = Path.Combine(rootPath ?? FileSystem.AppDataDirectory, AppConstants.Folders.Trips);
    }

    public async Task<decimal?> GetRateAsync(string tripSlug, string currency, DateTime date)
    {
        if (currency == "EUR") return 1.0m;
        
        // Ensure we don't look into the future beyond today
        var effectiveDate = date.Date > DateTime.Today ? DateTime.Today : date.Date;

        var cache = await GetMonthlyCacheAsync(tripSlug, effectiveDate);
        if (cache != null && cache.Rates.TryGetValue(effectiveDate.ToString("yyyy-MM-dd"), out var dayRates))
        {
            if (dayRates.TryGetValue(currency, out var rate))
            {
                return rate;
            }
        }

        // Fallback to last available rate in the same month or previous months?
        // The requirement says: "when a transaction is in the future, the app must consider the most recent exchange rate available"
        // And "fallback to last available rate" for weekends.
        
        return await FindLastAvailableRateAsync(tripSlug, currency, effectiveDate);
    }

    public async Task<bool> AreRatesMissingAsync(string tripSlug, IEnumerable<DateTime> dates, IEnumerable<string> currencies)
    {
        var uniqueCurrencies = currencies.Where(c => c != "EUR").Distinct().ToList();
        if (!uniqueCurrencies.Any()) return false;

        var uniqueDates = dates.Select(d => d.Date > DateTime.Today ? DateTime.Today : d.Date).Distinct().ToList();

        foreach (var date in uniqueDates)
        {
            foreach (var currency in uniqueCurrencies)
            {
                var rate = await GetRateAsync(tripSlug, currency, date);
                if (rate == null) return true;
            }
        }

        return false;
    }

    public async Task DownloadMissingRatesAsync(string tripSlug, IEnumerable<DateTime> dates, IEnumerable<string> currencies)
    {
        var uniqueCurrencies = currencies.Where(c => c != "EUR").Distinct().ToList();
        if (!uniqueCurrencies.Any()) return;

        var uniqueDates = dates.Select(d => d.Date > DateTime.Today ? DateTime.Today : d.Date).Distinct().ToList();
        if (!uniqueDates.Any()) return;

        await _downloadSemaphore.WaitAsync();
        try
        {
            if (!await AreRatesMissingAsync(tripSlug, uniqueDates, uniqueCurrencies)) return;

            // fawazahmed0 API provides data per day. We fetch EUR as base.
            // URL format: https://cdn.jsdelivr.net/npm/@fawazahmed0/currency-api@{date}/v1/currencies/eur.json
            
            var parsedRates = new Dictionary<DateTime, Dictionary<string, decimal>>();

            foreach (var date in uniqueDates.OrderBy(d => d))
            {
                var dateStr = date.ToString("yyyy-MM-dd");
                var baseUrl = _httpClient.BaseAddress?.ToString() ?? "https://cdn.jsdelivr.net/npm/@fawazahmed0/currency-api@";
                var url = baseUrl.EndsWith("@") ? $"{baseUrl}{dateStr}/v1/currencies/eur.json" : $"{baseUrl.TrimEnd('/')}/{dateStr}/v1/currencies/eur.json";

                try
                {
                    var response = await _httpClient.GetAsync(url);
                    if (!response.IsSuccessStatusCode) continue;

                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    
                    if (doc.RootElement.TryGetProperty("eur", out var eurRates))
                    {
                        var dayRates = new Dictionary<string, decimal>();
                        foreach (var currency in uniqueCurrencies)
                        {
                            if (eurRates.TryGetProperty(currency.ToLowerInvariant(), out var rateProp))
                            {
                                dayRates[currency] = rateProp.GetDecimal();
                            }
                        }

                        if (dayRates.Any())
                        {
                            parsedRates[date] = dayRates;
                        }
                    }
                }
                catch
                {
                    // Continue to next date on failure
                }
            }

            if (parsedRates.Any())
            {
                await SaveRatesToCacheAsync(tripSlug, parsedRates);
            }
        }
        finally
        {
            _downloadSemaphore.Release();
        }
    }

    private async Task<decimal?> FindLastAvailableRateAsync(string tripSlug, string currency, DateTime date)
    {
        // Search backwards up to 10 days (covering long weekends/holidays)
        for (int i = 1; i <= 10; i++)
        {
            var checkDate = date.AddDays(-i);
            var cache = await GetMonthlyCacheAsync(tripSlug, checkDate);
            if (cache != null && cache.Rates.TryGetValue(checkDate.ToString("yyyy-MM-dd"), out var dayRates))
            {
                if (dayRates.TryGetValue(currency, out var rate))
                {
                    return rate;
                }
            }
        }
        return null;
    }

    private async Task<MonthlyRatesCache?> GetMonthlyCacheAsync(string tripSlug, DateTime date)
    {
        string cacheKey = $"{tripSlug}_{date:yyyy_MM}";
        if (_memoryCache.TryGetValue(cacheKey, out var memCache)) return memCache;

        var filePath = Path.Combine(_tripsPath, tripSlug, AppConstants.Folders.Cache, string.Format(AppConstants.Files.ExchangeRatesTemplate, date));
        if (!File.Exists(filePath)) return null;

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var cache = JsonSerializer.Deserialize<MonthlyRatesCache>(json, _jsonOptions);
            if (cache != null)
            {
                _memoryCache[cacheKey] = cache;
            }
            return cache;
        }
        catch
        {
            return null;
        }
    }

    private async Task SaveRatesToCacheAsync(string tripSlug, Dictionary<DateTime, Dictionary<string, decimal>> ratesByDate)
    {
        var groups = ratesByDate.GroupBy(r => new { r.Key.Year, r.Key.Month });

        foreach (var group in groups)
        {
            var date = new DateTime(group.Key.Year, group.Key.Month, 1);
            var cache = await GetMonthlyCacheAsync(tripSlug, date) ?? new MonthlyRatesCache();

            foreach (var item in group)
            {
                cache.Rates[item.Key.ToString("yyyy-MM-dd")] = item.Value;
            }

            var cacheDir = Path.Combine(_tripsPath, tripSlug, AppConstants.Folders.Cache);
            if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);

            var filePath = Path.Combine(cacheDir, string.Format(AppConstants.Files.ExchangeRatesTemplate, date));
            var json = JsonSerializer.Serialize(cache, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json);

            string cacheKey = $"{tripSlug}_{date:yyyy_MM}";
            _memoryCache[cacheKey] = cache;
        }
    }

    private class MonthlyRatesCache
    {
        [JsonPropertyName("rates")]
        public Dictionary<string, Dictionary<string, decimal>> Rates { get; set; } = new();
    }
}
