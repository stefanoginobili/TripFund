using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using TripFund.App.Models;
using TripFund.App.Services;
using TripFund.App.Utilities;

namespace TripFund.App.Components.Common
{
    public partial class TransactionModal
    {
        [Inject] private LocalTripStorageService Storage { get; set; } = default!;
        [Inject] private NavigationManager Nav { get; set; } = default!;
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
        [Inject] private IThumbnailService Thumbnails { get; set; } = default!;

        [Parameter] public bool IsVisible { get; set; }
        [Parameter] public Transaction? Transaction { get; set; }
        [Parameter] public TripConfig? Config { get; set; }
        [Parameter] public string TripSlug { get; set; } = "";
        [Parameter] public EventCallback OnClose { get; set; }
        [Parameter] public EventCallback<Transaction> OnEdit { get; set; }

        private bool canEdit = true;
        private bool isReadonly = false;
        private List<AttachmentPreview> previews = new();

        protected override async Task OnParametersSetAsync()
        {
            if (IsVisible && Transaction != null && Config != null)
            {
                canEdit = Transaction.Split.Keys.All(slug => Config.Members.ContainsKey(slug));
                
                var registry = await Storage.GetTripRegistryAsync();
                if (registry != null && registry.Trips.TryGetValue(TripSlug, out var entry))
                {
                    isReadonly = entry.RemoteStorage?.Readonly ?? false;
                }
                
                await LoadPreviews();
            }
        }

        private async Task LoadPreviews()
        {
            if (Transaction == null) return;
            previews.Clear();

            TimeZoneInfo tz;
            try
            {
                tz = string.IsNullOrEmpty(Transaction.Timezone) ? TimeZoneInfo.Local : TimeZoneInfo.FindSystemTimeZoneById(Transaction.Timezone);
            }
            catch
            {
                tz = TimeZoneInfo.Local;
            }

            foreach (var att in Transaction.Attachments)
            {
                var fileName = att.Name;
                var path = await Storage.GetAttachmentPath(TripSlug, Transaction.Id, fileName);
                
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
                    var nativeThumb = await Thumbnails.GetThumbnailBase64Async(path);
                    if (!string.IsNullOrEmpty(nativeThumb))
                    {
                        preview.PreviewUrl = nativeThumb;
                        preview.IsImage = true;
                    }
                    else if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".gif" || ext == ".webp")
                    {
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

        private async Task Close()
        {
            await OnClose.InvokeAsync();
        }

        private async Task Edit()
        {
            if (Transaction != null)
            {
                await OnEdit.InvokeAsync(Transaction);
            }
        }

        private async Task OpenMap()
        {
            if (Transaction?.Location != null)
            {
                await Map.Default.OpenAsync(Transaction.Location.Latitude, Transaction.Location.Longitude, new MapLaunchOptions
                {
                    Name = Transaction.Location.Name,
                    NavigationMode = NavigationMode.None
                });
            }
        }

        private async Task OpenAttachment(string fileName)
        {
            if (Transaction == null) return;
            var path = await Storage.GetAttachmentPath(TripSlug, Transaction.Id, fileName);
            if (!string.IsNullOrEmpty(path))
            {
                await Launcher.Default.OpenAsync(new OpenFileRequest
                {
                    File = new ReadOnlyFile(path)
                });
            }
        }

        private string FormatCurrency(decimal amount, string currencyCode)
        {
            if (Config != null && Config.Currencies.TryGetValue(currencyCode, out var c))
            {
                string format = c.Decimals > 0 ? "N" + c.Decimals : "N0";
                return $"{c.Symbol} {amount.ToString(format)}";
            }
            return $"{amount:N2} {currencyCode}";
        }

        private string FormatAmountWithCode(decimal amount, string currencyCode)
        {
            if (Config != null && Config.Currencies.TryGetValue(currencyCode, out var c))
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
