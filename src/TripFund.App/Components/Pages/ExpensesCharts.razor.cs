using Microsoft.AspNetCore.Components;
using System.Globalization;
using TripFund.App.Models;
using TripFund.App.Services;
using TripFund.App.Constants;

namespace TripFund.App.Components.Pages
{
    public partial class ExpensesCharts
    {
        [Inject] private LocalStorageService Storage { get; set; } = default!;
        [Inject] private IExchangeRateService ExchangeRates { get; set; } = default!;

        [Parameter] public string tripSlug { get; set; } = "";

        private TripConfig? config;
        private List<CurrencyChartData> currencyCharts = new();
        private CurrencyChartData? globalChart;
        private List<Transaction> tripExpenses = new();
        private bool areRatesMissing;
        private bool isDownloadingRates;
        private List<string> missingRatesList = new();

        private bool someCurrenciesSkipped;

        protected override async Task OnInitializedAsync()
        {
            config = await Storage.GetLocalTripStorage(tripSlug).GetTripConfigAsync();
            if (config == null) return;

            var transactions = await Storage.GetLocalTripStorage(tripSlug).GetTransactionsAsync();
            tripExpenses = transactions.Where(t => t.Type == "expense").ToList();

            if (!tripExpenses.Any()) return;

            await LoadChartsAsync();
        }

        private async Task LoadChartsAsync()
        {
            currencyCharts.Clear();
            globalChart = null;
            areRatesMissing = false;
            someCurrenciesSkipped = false;

            if (!tripExpenses.Any()) return;

            // Individual currency charts
            var byCurrency = tripExpenses.GroupBy(e => e.Currency);
            foreach (var group in byCurrency)
            {
                var chart = CreateChartData(group.Key, group.ToList());
                if (chart != null) currencyCharts.Add(chart);
            }

            // Global chart if more than one currency is available in the trip
            if (config?.Currencies.Count > 1)
            {
                var currenciesNeeded = tripExpenses.Select(e => e.Currency).Distinct().ToList();
                var datesNeeded = tripExpenses.Select(e => e.Date.DateTime).Distinct().ToList();

                areRatesMissing = await ExchangeRates.AreRatesMissingAsync(tripSlug, datesNeeded, currenciesNeeded);

                if (areRatesMissing)
                {
                    missingRatesList.Clear();
                    var uniquePairs = tripExpenses
                        .Where(e => e.Currency != "EUR")
                        .Select(e => new { e.Currency, Date = e.Date.Date })
                        .Distinct()
                        .OrderBy(x => x.Date)
                        .ToList();

                    foreach (var pair in uniquePairs)
                    {
                        var rate = await ExchangeRates.GetRateAsync(tripSlug, pair.Currency, pair.Date);
                        if (rate == null)
                        {
                            missingRatesList.Add($"{pair.Currency} in data {pair.Date:dd/MM/yyyy}");
                        }
                    }
                }
                else
                {
                    var convertedExpenses = new List<Transaction>();
                    foreach (var e in tripExpenses)
                    {
                        var rate = await ExchangeRates.GetRateAsync(tripSlug, e.Currency, e.Date.DateTime);
                        if (rate.HasValue && rate.Value > 0)
                        {
                            convertedExpenses.Add(new Transaction
                            {
                                Category = e.Category,
                                Amount = e.Amount / rate.Value,
                                Currency = "EUR"
                            });
                        }
                        else if (e.Currency != "EUR")
                        {
                            someCurrenciesSkipped = true;
                        }
                    }
                    globalChart = CreateChartData("EUR", convertedExpenses);
                }
            }
        }

        private CurrencyChartData? CreateChartData(string currencyCode, List<Transaction> expenses)
        {
            var totalAmount = expenses.Sum(e => e.Amount);
            if (totalAmount <= 0) return null;

            var iso = IsoCurrencies.All.FirstOrDefault(c => c.Code.Equals(currencyCode, StringComparison.OrdinalIgnoreCase));
            var chartData = new CurrencyChartData 
            { 
                CurrencyCode = currencyCode,
                DisplayTitle = iso != null ? $"Spese in {iso.Name}\n({currencyCode})" : $"Spese in {currencyCode}"
            };
            var byCategory = expenses.GroupBy(e => e.Category);
            double currentAngle = 0;

            foreach (var catGroup in byCategory)
            {
                var slug = catGroup.Key;
                var amount = catGroup.Sum(e => e.Amount);
                var percentage = (double)(amount / totalAmount);

                var slice = new PieSlice
                {
                    Amount = amount,
                    Percentage = percentage
                };

                if (string.IsNullOrEmpty(slug))
                {
                    slice.Label = AppConstants.Categories.DefaultName;
                    slice.Icon = AppConstants.Categories.DefaultIcon;
                    slice.Color = AppConstants.Categories.DefaultColor;
                }
                else if (config?.Categories.Expenses.TryGetValue(slug, out var cat) == true)
                {
                    slice.Label = cat.Name;
                    slice.Icon = cat.Icon;
                    slice.Color = cat.Color;
                }
                else
                {
                    slice.Label = slug ?? "Senza categoria";
                    slice.Icon = AppConstants.Categories.UnknownIcon;
                    slice.Color = GetDeterministicGray(slug ?? "unknown");
                }

                double endAngle = currentAngle + (percentage * 2 * Math.PI);
                slice.SvgPath = GetSvgPath(currentAngle, endAngle);
                currentAngle = endAngle;

                chartData.Slices.Add(slice);
            }

            return chartData;
        }

        private async Task HandleDownloadRates()
        {
            if (isDownloadingRates) return;
            isDownloadingRates = true;

            try
            {
                var currenciesNeeded = tripExpenses.Select(e => e.Currency).Distinct();
                var datesNeeded = tripExpenses.Select(e => e.Date.DateTime).Distinct();
                await ExchangeRates.DownloadMissingRatesAsync(tripSlug, datesNeeded, currenciesNeeded);
                await LoadChartsAsync();
            }
            finally
            {
                isDownloadingRates = false;
            }
        }

        private string GetSvgPath(double startAngle, double endAngle)
        {
            const double cx = 50;
            const double cy = 50;
            const double r = 45;

            var x1 = cx + r * Math.Cos(startAngle);
            var y1 = cy + r * Math.Sin(startAngle);
            var x2 = cx + r * Math.Cos(endAngle);
            var y2 = cy + r * Math.Sin(endAngle);

            var largeArc = (endAngle - startAngle) > Math.PI ? 1 : 0;

            return $"M {cx} {cy} L {x1.ToString(CultureInfo.InvariantCulture)} {y1.ToString(CultureInfo.InvariantCulture)} A {r} {r} 0 {largeArc} 1 {x2.ToString(CultureInfo.InvariantCulture)} {y2.ToString(CultureInfo.InvariantCulture)} Z";
        }

        private string GetDeterministicGray(string input)
        {
            int hash = input.GetHashCode();
            int val = 100 + (Math.Abs(hash) % 101); // 100 to 200
            string hex = val.ToString("X2");
            return $"#{hex}{hex}{hex}";
        }

        private string FormatCurrency(decimal amount, string currencyCode)
        {
            if (currencyCode == "EUR")
            {
                return $"€ {amount:N2}";
            }

            if (config != null && config.Currencies.TryGetValue(currencyCode, out var c))
            {
                string format = c.Decimals > 0 ? "N" + c.Decimals : "N0";
                return $"{c.Symbol} {amount.ToString(format)}";
            }
            return $"{amount:N2} {currencyCode}";
        }

        private class CurrencyChartData
        {
            public string CurrencyCode { get; set; } = "";
            public string DisplayTitle { get; set; } = "";
            public List<PieSlice> Slices { get; set; } = new();
        }

        private class PieSlice
        {
            public string Label { get; set; } = "";
            public string Icon { get; set; } = "";
            public string Color { get; set; } = "";
            public decimal Amount { get; set; }
            public double Percentage { get; set; }
            public string SvgPath { get; set; } = "";
        }
    }
}
