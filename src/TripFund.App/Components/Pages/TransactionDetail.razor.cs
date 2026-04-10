using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using TripFund.App.Models;
using TripFund.App.Services;
using TripFund.App.Utilities;

namespace TripFund.App.Components.Pages
{
    public partial class TransactionDetail
    {
        [Inject] private LocalTripStorageService Storage { get; set; } = default!;
        [Inject] private NavigationManager Nav { get; set; } = default!;
        [Inject] private IAlertService Alerts { get; set; } = default!;
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
        [Inject] private IThumbnailService Thumbnails { get; set; } = default!;

        [Parameter] public string tripSlug { get; set; } = "";
        [Parameter] public string transactionId { get; set; } = "";
        [SupplyParameterFromQuery] public string? currency { get; set; }

        private TripConfig? config;
        private Transaction? transaction;
        private string deviceId = "";
        private bool isMenuOpen = false;
        private bool canEdit = true;
        private List<AttachmentPreview> previews = new();

        protected override async Task OnInitializedAsync()
        {
            config = await Storage.GetTripConfigAsync(tripSlug);
            transaction = await Storage.GetLatestTransactionVersionAsync(tripSlug, transactionId);

            if (transaction != null && config != null)
            {
                canEdit = transaction.Split.Keys.All(slug => config.Members.ContainsKey(slug));
                await LoadPreviews();
            }

            var settings = await Storage.GetAppSettingsAsync();
            if (settings != null)
            {
                deviceId = settings.DeviceId;
            }
        }

        private async Task LoadPreviews()
        {
            if (transaction == null) return;
            previews.Clear();

            TimeZoneInfo tz;
            try
            {
                tz = string.IsNullOrEmpty(transaction.Timezone) ? TimeZoneInfo.Local : TimeZoneInfo.FindSystemTimeZoneById(transaction.Timezone);
            }
            catch
            {
                tz = TimeZoneInfo.Local;
            }

            foreach (var att in transaction.Attachments)
            {
                var fileName = att.Name;
                var path = await Storage.GetAttachmentPath(tripSlug, transactionId, fileName);
                
                var localizedTime = TimeZoneInfo.ConvertTimeFromUtc(att.CreatedAt, tz);
                var preview = new AttachmentPreview 
                { 
                    FileName = fileName, 
                    OriginalName = att.OriginalName,
                    DisplayTimestamp = localizedTime.ToString("dd/MM/yyyy HH:mm")
                };
                if (!string.IsNullOrEmpty(path))
                {
                    var ext = Path.GetExtension(fileName).ToLower();
                    // Try to get native thumbnail first (covers images and documents on supporting platforms)
                    var nativeThumb = await Thumbnails.GetThumbnailBase64Async(path);
                    if (!string.IsNullOrEmpty(nativeThumb))
                    {
                        preview.PreviewUrl = nativeThumb;
                        preview.IsImage = true;
                    }
                    else if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".gif" || ext == ".webp")
                    {
                        // Fallback for images if native generator failed but it is an image
                        try
                        {
                            var bytes = await File.ReadAllBytesAsync(path);
                            var base64 = Convert.ToBase64String(bytes);
                            preview.PreviewUrl = $"data:image/{ext.TrimStart('.')};base64,{base64}";
                            preview.IsImage = true;
                        }
                        catch { /* ignore */ }
                    }
                }
                previews.Add(preview);
            }
        }

        private void ToggleMenu() => isMenuOpen = !isMenuOpen;

        private async Task OpenMap()
        {
            if (transaction?.Location != null)
            {
                await Map.Default.OpenAsync(transaction.Location.Latitude, transaction.Location.Longitude, new MapLaunchOptions
                {
                    Name = transaction.Location.Name,
                    NavigationMode = NavigationMode.None
                });
            }
        }

        private async Task OpenAttachment(string fileName)
        {
            var path = await Storage.GetAttachmentPath(tripSlug, transactionId, fileName);
            if (!string.IsNullOrEmpty(path))
            {
                await Launcher.Default.OpenAsync(new OpenFileRequest
                {
                    File = new ReadOnlyFile(path)
                });
            }
        }

        private void EditTransaction()
        {
            isMenuOpen = false;
            if (transaction == null || !canEdit) return;
            
            var route = transaction.Type == "contribution" ? "add-contribution" : "add-expense";
            Nav.NavigateTo($"/trip/{tripSlug}/{route}?edit={transactionId}&currency={currency}");
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

        private string FormatAmountWithCode(decimal amount, string currencyCode)
        {
            if (config != null && config.Currencies.TryGetValue(currencyCode, out var c))
            {
                string format = c.Decimals > 0 ? "N" + c.Decimals : "N0";
                return $"{currencyCode} {amount.ToString(format)}";
            }
            return $"{currencyCode} {amount.ToString("N2")}";
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

        private string GetTimeZoneDisplayName(Transaction tx)
        {
            if (string.IsNullOrEmpty(tx.Timezone))
            {
                return TimeZoneMapper.GetItalianCityName(TimeZoneInfo.Local.Id);
            }

            return TimeZoneMapper.GetItalianCityName(tx.Timezone);
        }

        private class AttachmentPreview
        {
            public string FileName { get; set; } = "";
            public string OriginalName { get; set; } = "";
            public string DisplayTimestamp { get; set; } = "";
            public string? PreviewUrl { get; set; }
            public bool IsImage { get; set; }
        }
    }
}
