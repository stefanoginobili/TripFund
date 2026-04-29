using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using TripFund.App.Models;
using TripFund.App.Services;
using TripFund.App.Utilities;
using TripFund.App.Constants;

namespace TripFund.App.Components.Pages
{
    public partial class MemberDashboard
    {
        [Inject] private LocalStorageService Storage { get; set; } = default!;
        [Inject] private IEmailService EmailService { get; set; } = default!;
        [Inject] private IAlertService AlertService { get; set; } = default!;
        [Inject] private NavigationManager Nav { get; set; } = default!;
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

        [Parameter] public string tripSlug { get; set; } = "";
        [Parameter] public string memberSlug { get; set; } = "";
        [SupplyParameterFromQuery] public string? currency { get; set; }
        
        private TripConfig? config;
        private User? member;
        private List<Transaction> allTransactions = new();
        private List<UserTransactionItem> userTransactions = new();
        
        private string selectedCurrency = "";
        private bool isMissing = false;
        private bool isReadonly = false;
        
        private decimal totalContributed = 0;
        private decimal remainingBalance = 0;
        private decimal targetAmount = 0;
        private double progressPercentage = 0;
        private bool isExceeded => totalContributed > targetAmount;

        private bool HasContributions => allTransactions.Any(t => t.Type == "contribution" && t.Split.ContainsKey(memberSlug));

        private Transaction? selectedTransaction;
        private bool isTransactionModalOpen = false;

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
                allTransactions = await Storage.GetLocalTripStorage(tripSlug).GetTransactionsAsync();

                if (config.Members.TryGetValue(memberSlug, out var m))
                {
                    member = m;
                    isMissing = false;
                }
                else if (allTransactions.Any(t => t.Split.ContainsKey(memberSlug)))
                {
                    member = new User
                    {
                        Name = memberSlug,
                        Avatar = "❓",
                        Email = ""
                    };
                    isMissing = true;
                }

                if (!string.IsNullOrEmpty(currency) && config.Currencies.ContainsKey(currency))
                {
                    selectedCurrency = currency;
                }
                else if (config.Currencies.Any())
                {
                    selectedCurrency = config.Currencies.Keys.First();
                }
                CalculateStats();
            }
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

        private void EditTransaction(Transaction t)
        {
            isTransactionModalOpen = false;
            var route = t.Type == "contribution" ? "contribution" : "expense";
            Nav.NavigateTo($"/trip/{tripSlug}/{route}?edit={t.Id}&currency={selectedCurrency}&member={memberSlug}");
        }

        private void NavigateToContribution()
        {
            if (isMissing) return;
            Nav.NavigateTo($"/trip/{tripSlug}/contribution?member={memberSlug}&currency={selectedCurrency}");
        }

        private async Task SendSummaryEmail()
        {
            if (config == null || member == null) return;

            var latestContribution = allTransactions
                .Where(t => t.Type == "contribution" && t.Split.ContainsKey(memberSlug))
                .OrderByDescending(t => t.Date)
                .FirstOrDefault();

            try
            {
                var body = ReceiptGenerator.GenerateContributionText(config, memberSlug, latestContribution, allTransactions);
                var recipients = string.IsNullOrEmpty(member.Email) ? Array.Empty<string>() : new[] { member.Email };
                await EmailService.SendEmailAsync($"{config.Name} - Riepilogo versamenti", body, recipients);
            }
            catch (Exception ex)
            {
                await AlertService.ShowAlertAsync("Errore Email", "Non è stato possibile preparare l'email: " + ex.Message, type: AlertType.Error);
            }
        }

        private void CalculateStats()
        {
            if (config == null || member == null || string.IsNullOrEmpty(selectedCurrency)) return;

            var currencyConfig = config.Currencies.ContainsKey(selectedCurrency) ? config.Currencies[selectedCurrency] : null;
            if (currencyConfig == null) return;

            targetAmount = currencyConfig.ExpectedQuotaPerMember;

            var currentCurrencyTx = allTransactions.Where(t => t.Currency == selectedCurrency).ToList();
            
            userTransactions.Clear();
            totalContributed = 0;
            decimal mExpenses = 0;

            foreach (var t in currentCurrencyTx)
            {
                if (t.Split.TryGetValue(memberSlug, out SplitInfo? split) && split.Amount > 0)
                {
                    userTransactions.Add(new UserTransactionItem { OriginalTx = t, UserAmount = split.Amount });
                    
                    if (t.Type == "contribution") totalContributed += split.Amount;
                    else if (t.Type == "expense") mExpenses += split.Amount;
                }
            }

            remainingBalance = totalContributed - mExpenses;
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
            var iso = IsoCurrencies.All.FirstOrDefault(c => c.Code.Equals(currencyCode, System.StringComparison.OrdinalIgnoreCase));
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

        private class UserTransactionItem
        {
            public Transaction OriginalTx { get; set; } = new();
            public decimal UserAmount { get; set; }
        }
    }
}
