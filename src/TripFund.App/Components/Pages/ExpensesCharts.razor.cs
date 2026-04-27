using Microsoft.AspNetCore.Components;
using System.Globalization;
using TripFund.App.Models;
using TripFund.App.Services;
using TripFund.App.Constants;

namespace TripFund.App.Components.Pages
{
    public partial class ExpensesCharts
    {
        [Inject] private LocalTripStorageService Storage { get; set; } = default!;
        [Inject] private NavigationManager Nav { get; set; } = default!;

        [Parameter] public string tripSlug { get; set; } = "";

        private TripConfig? config;
        private List<CurrencyChartData> currencyCharts = new();

        protected override async Task OnInitializedAsync()
        {
            config = await Storage.GetTripConfigAsync(tripSlug);
            if (config == null) return;

            var transactions = await Storage.GetTransactionsAsync(tripSlug);
            var expenses = transactions.Where(t => t.Type == "expense").ToList();

            if (!expenses.Any()) return;

            // Group by currency
            var byCurrency = expenses.GroupBy(e => e.Currency);

            foreach (var group in byCurrency)
            {
                var currencyCode = group.Key;
                var totalAmount = group.Sum(e => e.Amount);
                if (totalAmount <= 0) continue;

                var chartData = new CurrencyChartData { CurrencyCode = currencyCode };
                
                // Group by category
                var byCategory = group.GroupBy(e => e.Category);
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

                    if (string.IsNullOrEmpty(slug) || slug == AppConstants.Categories.DefaultSlug)
                    {
                        slice.Label = AppConstants.Categories.DefaultName;
                        slice.Icon = AppConstants.Categories.DefaultIcon;
                        slice.Color = AppConstants.Categories.DefaultColor;
                    }
                    else if (config.Categories.Expenses.TryGetValue(slug, out var cat))
                    {
                        slice.Label = cat.Name;
                        slice.Icon = cat.Icon;
                        slice.Color = cat.Color;
                    }
                    else
                    {
                        slice.Label = slug;
                        slice.Icon = AppConstants.Categories.UnknownIcon;
                        slice.Color = GetDeterministicGray(slug);
                    }

                    // Calculate SVG path
                    double endAngle = currentAngle + (percentage * 2 * Math.PI);
                    slice.SvgPath = GetSvgPath(currentAngle, endAngle);
                    currentAngle = endAngle;

                    chartData.Slices.Add(slice);
                }

                currencyCharts.Add(chartData);
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
