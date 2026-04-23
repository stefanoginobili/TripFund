using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using TripFund.App.Utilities;

namespace TripFund.App.Components.Common
{
    public partial class TimeZoneSelector
    {
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

        [Parameter] public string Label { get; set; } = "Fuso Orario";
        [Parameter] public string Value { get; set; } = string.Empty;
        [Parameter] public EventCallback<string> ValueChanged { get; set; }
        [Parameter] public string Class { get; set; } = string.Empty;

        private List<TimeZoneInfo> _timeZones = new();
        private bool _shouldScroll = false;

        protected override void OnInitialized()
        {
            _timeZones = TimeZoneInfo.GetSystemTimeZones()
                .Where(tz => TimeZoneMapper.IsSupported(tz.Id))
                .OrderBy(tz => tz.BaseUtcOffset)
                .ToList();
                
            if (string.IsNullOrEmpty(Value))
            {
                Value = _timeZones.FirstOrDefault(t => t.Id == TimeZoneInfo.Local.Id)?.Id 
                        ?? _timeZones.FirstOrDefault()?.Id 
                        ?? TimeZoneInfo.Utc.Id;
            }
        }

        private async Task OpenDropdown()
        {
            _shouldScroll = true;
            await Task.CompletedTask;
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (_shouldScroll)
            {
                _shouldScroll = false;
                await JSRuntime.InvokeVoidAsync("appLogic.scrollIntoView", "#selected-tz-item", "center");
            }
        }

        private async Task SelectTimeZone(string id)
        {
            Value = id;
            await ValueChanged.InvokeAsync(id);
        }

        private string GetFormattedDisplayName(TimeZoneInfo? tz)
        {
            if (tz == null) return string.Empty;
            
            var offset = tz.BaseUtcOffset;
            var sign = offset.Ticks >= 0 ? "+" : "-";
            var offsetString = $"(UTC{sign}{Math.Abs(offset.Hours):D2}:{Math.Abs(offset.Minutes):D2})";
            
            var displayName = tz.DisplayName;
            if (displayName.StartsWith(offsetString))
            {
                return displayName;
            }
            
            return $"{offsetString} {displayName}";
        }
    }
}
