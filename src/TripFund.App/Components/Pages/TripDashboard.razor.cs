using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using TripFund.App.Models;
using TripFund.App.Services;
using TripFund.App.Utilities;
using TripFund.App.Constants;

namespace TripFund.App.Components.Pages
{
    public partial class TripDashboard
    {
        [Inject] private LocalStorageService Storage { get; set; } = default!;
        [Inject] private NavigationManager Nav { get; set; } = default!;
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
        [Inject] private IRemoteStorageService RemoteStorage { get; set; } = default!;
        [Inject] private PdfReportService PdfService { get; set; } = default!;

        [Parameter] public string tripSlug { get; set; } = "";
        [SupplyParameterFromQuery] public string? currency { get; set; }
        
        private TripConfig? config;
        private List<Transaction> transactions = new();
        
        private string selectedCurrency = "";
        private bool isReadonly = false;
        
        private decimal totalContributed = 0;
        private decimal totalRemaining = 0;
        private decimal targetAmount = 0;
        private double progressPercentage = 0;
        private bool isExceeded => totalContributed > targetAmount;

        private List<MemberStats> membersStats = new();

        private Transaction? selectedTransaction;
        private bool isTransactionModalOpen = false;
        private bool isMembersModalOpen = false;

        protected override async Task OnInitializedAsync()
        {
            config = await Storage.GetLocalTripStorage(tripSlug).GetTripConfigAsync();
            
            var registry = await Storage.GetTripRegistryAsync();
            if (registry != null && registry.Trips.TryGetValue(tripSlug, out var entry))
            {
                isReadonly = (entry.RemoteStorage?.Readonly ?? false) || await Storage.GetLocalTripStorage(tripSlug).HasConflictsAsync();
            }

            if (config != null)
            {
                if (!string.IsNullOrEmpty(currency) && config.Currencies.ContainsKey(currency))
                {
                    selectedCurrency = currency;
                }
                else if (config.Currencies.Any())
                {
                    selectedCurrency = config.Currencies.Keys.First();
                }
                transactions = await Storage.GetLocalTripStorage(tripSlug).GetTransactionsAsync();
                CalculateStats();
            }
        }

        private async Task OnSyncCompleted()
        {
            config = await Storage.GetLocalTripStorage(tripSlug).GetTripConfigAsync();
            
            var registry = await Storage.GetTripRegistryAsync();
            if (registry != null && registry.Trips.TryGetValue(tripSlug, out var entry))
            {
                isReadonly = (entry.RemoteStorage?.Readonly ?? false) || await Storage.GetLocalTripStorage(tripSlug).HasConflictsAsync();
            }

            transactions = await Storage.GetLocalTripStorage(tripSlug).GetTransactionsAsync();
            CalculateStats();
            StateHasChanged();
        }

        private void SelectCurrency(string currencyCode)
        {
            selectedCurrency = currencyCode;
            CalculateStats();
        }

        private void OpenTransactionModal(Transaction t)
        {
            selectedTransaction = t;
            isTransactionModalOpen = true;
        }

        private void CloseTransactionModal()
        {
            isTransactionModalOpen = false;
        }

        private void OpenMemberDashboard(string slug)
        {
            isMembersModalOpen = false;
            Nav.NavigateTo($"/trip/{tripSlug}/member/{slug}?currency={selectedCurrency}");
        }

        private async Task GenerateReport()
        {
            if (config == null) return;
            
            try
            {
                var filePath = await PdfService.GenerateExpenseReportAsync(config, transactions);
                await Launcher.Default.OpenAsync(new OpenFileRequest
                {
                    File = new ReadOnlyFile(filePath)
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating report: {ex}");
                // In a real app, show an alert to the user.
            }
        }

        private void EditTransaction(Transaction t)
        {
            isTransactionModalOpen = false;
            var route = t.Type == "contribution" ? "contribution" : "expense";
            Nav.NavigateTo($"/trip/{tripSlug}/{route}?edit={t.Id}&currency={selectedCurrency}");
        }

        private void CalculateStats()
        {
            if (config == null || string.IsNullOrEmpty(selectedCurrency)) return;

            var currencyConfig = config.Currencies.ContainsKey(selectedCurrency) ? config.Currencies[selectedCurrency] : null;
            if (currencyConfig == null) return;

            var currentCurrencyTx = transactions.Where(t => t.Currency == selectedCurrency).ToList();
            
            targetAmount = currencyConfig.ExpectedQuotaPerMember * config.Members.Count;

            totalContributed = currentCurrencyTx.Where(t => t.Type == "contribution").Sum(t => t.Amount);
            decimal totalExpenses = currentCurrencyTx.Where(t => t.Type == "expense").Sum(t => t.Amount);
            totalRemaining = totalContributed - totalExpenses;

            membersStats.Clear();

            // Standard members
            foreach (var member in config.Members)
            {
                decimal mContributed = 0;
                decimal mExpenses = 0;

                foreach (var t in currentCurrencyTx)
                {
                    if (t.Split.TryGetValue(member.Key, out SplitInfo? split))
                    {
                        if (t.Type == "contribution") mContributed += split.Amount;
                        else if (t.Type == "expense") mExpenses += split.Amount;
                    }
                }

                decimal mRemaining = mContributed - mExpenses;
                
                membersStats.Add(new MemberStats
                {
                    Slug = member.Key,
                    Name = member.Value.Name,
                    Avatar = member.Value.Avatar,
                    TotalContributed = mContributed,
                    RemainingBalance = mRemaining,
                    IsMissing = false
                });
            }

            // Missing members (present in transactions but not in config)
            var transactionSlugs = currentCurrencyTx.SelectMany(t => t.Split.Keys).Distinct();
            var missingSlugs = transactionSlugs.Where(s => !config.Members.ContainsKey(s)).ToList();

            foreach (var slug in missingSlugs)
            {
                decimal mContributed = 0;
                decimal mExpenses = 0;

                foreach (var t in currentCurrencyTx)
                {
                    if (t.Split.TryGetValue(slug, out SplitInfo? split))
                    {
                        if (t.Type == "contribution") mContributed += split.Amount;
                        else if (t.Type == "expense") mExpenses += split.Amount;
                    }
                }

                decimal mRemaining = mContributed - mExpenses;

                membersStats.Add(new MemberStats
                {
                    Slug = slug,
                    Name = slug,
                    Avatar = "❓",
                    TotalContributed = mContributed,
                    RemainingBalance = mRemaining,
                    IsMissing = true
                });
            }

            progressPercentage = targetAmount > 0 ? Math.Min(100, (double)(totalContributed / targetAmount * 100)) : 0;
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

        private string GetCurrencyName(string currencyCode)
        {
            var iso = IsoCurrencies.All.FirstOrDefault(c => c.Code.Equals(currencyCode, StringComparison.OrdinalIgnoreCase));
            return iso?.Name ?? "";
        }

        private string GetDisplayDate(Transaction tx)
        {
            DateTimeOffset displayDate;
            TimeZoneInfo tz;
            
            if (string.IsNullOrEmpty(tx.Timezone))
            {
                tz = TimeZoneInfo.Local;
                displayDate = tx.Date.ToLocalTime();
            }
            else
            {
                try
                {
                    tz = TimeZoneInfo.FindSystemTimeZoneById(tx.Timezone);
                    displayDate = TimeZoneInfo.ConvertTime(tx.Date, tz);
                }
                catch
                {
                    tz = TimeZoneInfo.Local;
                    displayDate = tx.Date.ToLocalTime();
                }
            }

            var offsetStr = TimeZoneMapper.GetFormattedOffset(tz, displayDate);
            
            return $"{displayDate.ToString("dd/MM/yyyy HH:mm")} {offsetStr}";
        }
    }
}
