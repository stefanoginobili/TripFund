using Microsoft.AspNetCore.Components;
using TripFund.App.Models;
using TripFund.App.Services;
using TripFund.App.Utilities;

namespace TripFund.App.Components.Common;

public partial class ConflictResolverModal
{
    [Inject] private LocalTripStorageService Storage { get; set; } = default!;

    [Parameter] public bool IsVisible { get; set; }
    [Parameter] public string TripSlug { get; set; } = string.Empty;
    [Parameter] public ConflictInfo? Conflict { get; set; }
    [Parameter] public EventCallback OnResolved { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    private List<ConflictVersion<TripConfig>>? configVersions;
    private List<ConflictVersion<Transaction>>? transactionVersions;
    private int? selectedIndex;
    private bool isSaving = false;
    private string currentDeviceId = string.Empty;

    private Dictionary<string, bool> diffMap = new();

    protected override async Task OnParametersSetAsync()
    {
        if (IsVisible && Conflict != null)
        {
            var conflictId = Conflict.Id;
            var conflictType = Conflict.Type;

            selectedIndex = null;
            diffMap.Clear();

            var settings = await Storage.GetAppSettingsAsync();
            currentDeviceId = settings?.DeviceId ?? string.Empty;

            if (conflictType == "config")
            {
                configVersions = await Storage.GetConflictingConfigVersionsAsync(TripSlug);
                CalculateConfigDiffs();
            }
            else
            {
                transactionVersions = await Storage.GetConflictingTransactionVersionsAsync(TripSlug, conflictId);
                CalculateTransactionDiffs();
            }
        }
    }

    private void CalculateConfigDiffs()
    {
        var versions = configVersions;
        if (versions == null || versions.Count < 2) return;

        diffMap["Name"] = versions.Select(v => v.Data?.Name).Distinct().Count() > 1;
        diffMap["Dates"] = versions.Select(v => v.Data == null ? "DELETED" : $"{v.Data.StartDate:d}-{v.Data.EndDate:d}").Distinct().Count() > 1;
        diffMap["Description"] = versions.Select(v => v.Data?.Description).Distinct().Count() > 1;
        diffMap["Members"] = versions.Select(v => NormalizeMembers(v.Data)).Distinct().Count() > 1;
        diffMap["Currencies"] = versions.Select(v => NormalizeCurrencies(v.Data)).Distinct().Count() > 1;
    }

    private void CalculateTransactionDiffs()
    {
        var versions = transactionVersions;
        if (versions == null || versions.Count < 2) return;

        diffMap["Amount"] = versions.Select(v => v.Data?.Amount).Distinct().Count() > 1;
        diffMap["Description"] = versions.Select(v => v.Data?.Description).Distinct().Count() > 1;
        diffMap["DateTime"] = versions.Select(v => v.Data == null ? "DELETED" : $"{v.Data.Date:O}-{v.Data.Timezone}").Distinct().Count() > 1;
        diffMap["Participant"] = versions.Select(v => v.Data?.Split?.Keys?.FirstOrDefault()).Distinct().Count() > 1;
        diffMap["Participants"] = versions.Select(v => NormalizeSplit(v.Data)).Distinct().Count() > 1;
        diffMap["Attachments"] = versions.Select(v => NormalizeAttachments(v.Data)).Distinct().Count() > 1;
        diffMap["Location"] = versions.Select(v => NormalizeLocation(v.Data)).Distinct().Count() > 1;
    }

    private string NormalizeMembers(TripConfig? c)
    {
        if (c == null) return "DELETED";
        return string.Join("|", (c.Members ?? new()).OrderBy(m => m.Key).Select(m => $"{m.Key}:{m.Value.Name}:{m.Value.Email}:{m.Value.Avatar}"));
    }

    private string NormalizeCurrencies(TripConfig? c)
    {
        if (c == null) return "DELETED";
        return string.Join("|", (c.Currencies ?? new()).OrderBy(curr => curr.Key).Select(curr => $"{curr.Key}:{curr.Value.Symbol}:{curr.Value.Name}:{curr.Value.Decimals}:{curr.Value.ExpectedQuotaPerMember}"));
    }

    private string NormalizeSplit(Transaction? t)
    {
        if (t == null) return "DELETED";
        return string.Join("|", (t.Split ?? new()).OrderBy(s => s.Key).Select(s => $"{s.Key}:{s.Value.Amount}:{s.Value.Manual}"));
    }

    private string NormalizeAttachments(Transaction? t)
    {
        if (t == null) return "DELETED";
        return string.Join("|", (t.Attachments ?? new()).OrderBy(a => a.Name).Select(a => $"{a.Name}:{a.OriginalName}:{a.CreatedAt:O}"));
    }

    private string NormalizeLocation(Transaction? t)
    {
        if (t == null) return "DELETED";
        if (t.Location == null) return "";
        return $"{t.Location.Name}:{t.Location.Latitude:F6}:{t.Location.Longitude:F6}";
    }

    private async Task Confirm()
    {
        if (!selectedIndex.HasValue || isSaving) return;
        isSaving = true;

        var settings = await Storage.GetAppSettingsAsync();
        var deviceId = settings?.DeviceId ?? "unknown";

        if (Conflict?.Type == "config" && configVersions != null)
        {
            var winner = configVersions[selectedIndex.Value].Data;
            await Storage.ResolveConfigConflictAsync(TripSlug, winner, deviceId);
        }
        else if (Conflict != null && transactionVersions != null)
        {
            var winner = transactionVersions[selectedIndex.Value].Data;
            await Storage.ResolveConflictAsync(TripSlug, Conflict.Id, winner, deviceId);
        }

        isSaving = false;
        await OnResolved.InvokeAsync();
        await OnClose.InvokeAsync();
    }

    private void SelectVersion(int index)
    {
        selectedIndex = index;
    }

    private async Task Close()
    {
        if (!isSaving)
        {
            await OnClose.InvokeAsync();
        }
    }

    private string GetDiffClass(string propertyName)
    {
        return diffMap.TryGetValue(propertyName, out var isDiff) && isDiff ? "text-danger" : "text-neutral";
    }

    private string GetTimeZoneDisplayName(Transaction? tx)
    {
        if (tx == null) return string.Empty;
        if (string.IsNullOrEmpty(tx.Timezone))
        {
            return TimeZoneMapper.GetItalianCityName(TimeZoneInfo.Local.Id);
        }

        return TimeZoneMapper.GetItalianCityName(tx.Timezone);
    }
}
