using Microsoft.AspNetCore.Components;
using TripFund.App.Models;
using TripFund.App.Services;
using TripFund.App.Utilities;

namespace TripFund.App.Components.Common;

public partial class ConflictResolverModal
{
    [Inject] private LocalTripStorageService Storage { get; set; } = default!;
    [Inject] private IAlertService AlertService { get; set; } = default!;

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
    private TripConfig? currentConfig;

    private static readonly System.Globalization.CultureInfo _itCulture = new("it-IT");

    private Dictionary<string, bool> diffMap = new();
    private Dictionary<(string Property, int VersionIndex), string> evaluationStrings = new();

    protected override async Task OnParametersSetAsync()
    {
        if (IsVisible && Conflict != null)
        {
            var conflictId = Conflict.Id;
            var conflictType = Conflict.Type;

            selectedIndex = null;
            diffMap.Clear();
            evaluationStrings.Clear();

            var settings = await Storage.GetAppSettingsAsync();
            currentDeviceId = settings?.DeviceId ?? string.Empty;
            currentConfig = await Storage.GetTripConfigAsync(TripSlug);

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

        var names = versions.Select(v => v.Data?.Name ?? "Nessun nome").ToList();
        diffMap["Name"] = names.Distinct().Count() > 1;
        for (int i = 0; i < versions.Count; i++) evaluationStrings[("Name", i)] = names[i];

        var dates = versions.Select(v => v.Data == null ? "ELIMINATA" : $"{v.Data.StartDate.ToString("dd/MM/yyyy", _itCulture)} - {v.Data.EndDate.ToString("dd/MM/yyyy", _itCulture)}").ToList();
        diffMap["Dates"] = dates.Distinct().Count() > 1;
        for (int i = 0; i < versions.Count; i++) evaluationStrings[("Dates", i)] = dates[i];

        var descriptions = versions.Select(v => v.Data?.Description ?? "Nessuna descrizione").ToList();
        diffMap["Description"] = descriptions.Distinct().Count() > 1;
        for (int i = 0; i < versions.Count; i++) evaluationStrings[("Description", i)] = descriptions[i];

        var members = versions.Select(v => NormalizeMembers(v.Data)).ToList();
        diffMap["Members"] = members.Distinct().Count() > 1;
        for (int i = 0; i < versions.Count; i++) evaluationStrings[("Members", i)] = members[i];

        var currencies = versions.Select(v => NormalizeCurrencies(v.Data)).ToList();
        diffMap["Currencies"] = currencies.Distinct().Count() > 1;
        for (int i = 0; i < versions.Count; i++) evaluationStrings[("Currencies", i)] = currencies[i];
    }

    private void CalculateTransactionDiffs()
    {
        var versions = transactionVersions;
        if (versions == null || versions.Count < 2) return;

        var amounts = versions.Select(v => v.Data == null ? "ELIMINATA" : $"{v.Data.Currency} {v.Data.Amount.ToString(_itCulture)}").ToList();
        diffMap["Amount"] = amounts.Distinct().Count() > 1;
        for (int i = 0; i < versions.Count; i++) evaluationStrings[("Amount", i)] = amounts[i];

        var descriptions = versions.Select(v => v.Data?.Description ?? "Nessuna descrizione").ToList();
        diffMap["Description"] = descriptions.Distinct().Count() > 1;
        for (int i = 0; i < versions.Count; i++) evaluationStrings[("Description", i)] = descriptions[i];

        var dateTimes = versions.Select(v => v.Data == null ? "ELIMINATA" : $"{v.Data.Date.ToString("dd/MM/yyyy HH:mm:ss", _itCulture)}").ToList();
        diffMap["DateTime"] = dateTimes.Distinct().Count() > 1;
        for (int i = 0; i < versions.Count; i++) 
        {
            var v = versions[i];
            evaluationStrings[("DateTime", i)] = v.Data == null ? "ELIMINATA" : $"Data: {v.Data.Date.ToString("dd/MM/yyyy HH:mm:ss", _itCulture)}<br />Fuso orario: {v.Data.Timezone}";
        }

        var participants = versions.Select(v => v.Data?.Split?.Keys?.FirstOrDefault() ?? "Nessuno").ToList();
        diffMap["Participant"] = participants.Distinct().Count() > 1;
        for (int i = 0; i < versions.Count; i++) evaluationStrings[("Participant", i)] = participants[i];

        var splits = versions.Select(v => NormalizeSplit(v.Data)).ToList();
        diffMap["Participants"] = splits.Distinct().Count() > 1;
        for (int i = 0; i < versions.Count; i++) evaluationStrings[("Participants", i)] = splits[i];

        var attachments = versions.Select(v => NormalizeAttachments(v.Data)).ToList();
        diffMap["Attachments"] = attachments.Distinct().Count() > 1;
        for (int i = 0; i < versions.Count; i++) evaluationStrings[("Attachments", i)] = attachments[i];

        var locations = versions.Select(v => NormalizeLocation(v.Data)).ToList();
        diffMap["Location"] = locations.Distinct().Count() > 1;
        for (int i = 0; i < versions.Count; i++) evaluationStrings[("Location", i)] = locations[i];
    }

    private string NormalizeMembers(TripConfig? c)
    {
        if (c == null) return "ELIMINATA";
        if (c.Members == null || !c.Members.Any()) return "Nessun partecipante";
        return string.Join("<br /><br />", c.Members.OrderBy(m => m.Key).Select(m => 
            $"{($"{m.Value.Avatar} {m.Value.Name}").Trim()} ({m.Key})<br />" +
            $"{(string.IsNullOrWhiteSpace(m.Value.Email) ? "nessuna email" : m.Value.Email)}"));
    }

    private string NormalizeCurrencies(TripConfig? c)
    {
        if (c == null) return "ELIMINATA";
        if (c.Currencies == null || !c.Currencies.Any()) return "Nessuna cassa";
        return string.Join("<br /><br />", c.Currencies.OrderBy(curr => curr.Key).Select(curr => 
            $"{curr.Key} ({curr.Value.Symbol})<br />" +
            $"Quota: {curr.Value.ExpectedQuotaPerMember.ToString("N" + curr.Value.Decimals, _itCulture)}"));
    }

    private string NormalizeSplit(Transaction? t)
    {
        if (t == null) return "ELIMINATA";
        if (t.Split == null || !t.Split.Any()) return "Nessun partecipante";
        
        return string.Join("<br /><br />", t.Split.OrderBy(s => s.Key).Select(s => 
            $"{s.Key}: {s.Value.Amount.ToString(_itCulture)} ({(s.Value.Manual ? "manuale" : "auto")})"));
    }

    private string NormalizeAttachments(Transaction? t)
    {
        if (t == null) return "ELIMINATA";
        if (t.Attachments == null || !t.Attachments.Any()) return "Nessun allegato";
        return string.Join("<br /><br />", t.Attachments.OrderBy(a => a.Name).Select(a => 
            $"{a.OriginalName}<br />" +
            $"{a.Name}<br />" +
            $"{a.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", _itCulture)}"));
    }

    private string NormalizeLocation(Transaction? t)
    {
        if (t == null) return "ELIMINATA";
        if (t.Location == null) return "Nessun luogo";
        return $"{t.Location.Name}<br />" +
               $"Latitudine: {t.Location.Latitude.ToString(_itCulture)}<br />" +
               $"Longitudine: {t.Location.Longitude.ToString(_itCulture)}";
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

    private async Task ShowDiffInfo(string propertyName, int versionIndex)
    {
        if (diffMap.TryGetValue(propertyName, out var isDiff) && isDiff)
        {
            if (evaluationStrings.TryGetValue((propertyName, versionIndex), out var evalString))
            {
                await AlertService.ShowAlertAsync("Dettaglio", evalString, "Chiudi", AlertType.Information, messageAlignment: "left");
            }
        }
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
        if (diffMap.TryGetValue(propertyName, out var isDiff) && isDiff)
        {
            return "text-danger clickable-diff";
        }
        return "text-neutral";
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
