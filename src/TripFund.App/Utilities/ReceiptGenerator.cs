using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using TripFund.App.Models;

namespace TripFund.App.Utilities;

public static class ReceiptGenerator
{
    private static readonly CultureInfo _itCulture = new CultureInfo("it-IT");

    public static string GenerateContributionText(TripConfig trip, Transaction contribution, List<Transaction> allTransactions)
    {
        var memberSlug = contribution.Split.Keys.First();
        var member = trip.Members[memberSlug];
        
        // All contributions by this member for this trip, sorted by date ASCENDING
        var memberContributions = allTransactions
            .Where(t => t.Type == "contribution" && t.Split.ContainsKey(memberSlug))
            .OrderBy(t => t.Date)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"Ciao {member.Name},"); 
        sb.AppendLine($"ecco il riepilogo aggiornato di tutti i tuoi versamenti per la cassa comune del viaggio \"{trip.Name}\".");
        sb.AppendLine();
        sb.AppendLine("--- DETTAGLIO VERSAMENTI ---");
        sb.AppendLine();

        foreach (var c in memberContributions)
        {
            var currency = trip.Currencies.TryGetValue(c.Currency, out var cur) ? cur : new Currency { Symbol = c.Currency, Decimals = 2 };
            
            var displayDate = c.Date.DateTime;
            TimeZoneInfo tz = TimeZoneInfo.Local;

            if (!string.IsNullOrEmpty(c.Timezone))
            {
                try
                {
                    tz = TimeZoneInfo.FindSystemTimeZoneById(c.Timezone);
                    displayDate = TimeZoneInfo.ConvertTime(c.Date, tz).DateTime;
                }
                catch
                {
                    // Fallback to the recorded DateTime
                    displayDate = c.Date.DateTime;
                }
            }

            var offsetStr = TimeZoneMapper.GetFormattedOffset(tz, c.Date);

            sb.AppendLine($"• Data: {displayDate.ToString("dd/MM/yyyy HH:mm", _itCulture)} {offsetStr}");

            sb.AppendLine($"  Importo: {c.Currency} {c.Split[memberSlug].Amount.ToString("N" + currency.Decimals, _itCulture)}");
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("--- RIEPILOGO TOTALI PER VALUTA ---");
        sb.AppendLine();

        // Group by currency to show totals
        var totalsByCurrency = memberContributions
            .GroupBy(t => t.Currency)
            .Select(g => new { 
                Currency = g.Key, 
                Total = g.Sum(t => t.Split[memberSlug].Amount) 
            })
            .ToList();

        foreach (var total in totalsByCurrency)
        {
            var currency = trip.Currencies.TryGetValue(total.Currency, out var cur) ? cur : new Currency { Symbol = total.Currency, Decimals = 2 };
            sb.AppendLine($"• Totale {total.Currency}: {total.Total.ToString("N" + currency.Decimals, _itCulture)}");
        }

        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("Inviato dall'app TripFund.");
        sb.AppendLine("In caso di dubbi, contatta il coordinatore del gruppo.");

        return sb.ToString();
    }
}
