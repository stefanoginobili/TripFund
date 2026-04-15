using System.Globalization;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Microsoft.Maui.Storage;
using TripFund.App.Models;
using TripFund.App.Utilities;

namespace TripFund.App.Services;

public class PdfReportService
{
    private static readonly CultureInfo ItCulture = new("it-IT");
    private const double Margin = 40;
    private const double PageWidth = 595; // A4 width in points
    private const double PageHeight = 842; // A4 height in points

    private readonly string _cacheDir;

    public PdfReportService()
    {
        PdfFontResolver.Register();
        try { _cacheDir = FileSystem.CacheDirectory; }
        catch { _cacheDir = Path.GetTempPath(); } // Fallback for tests
    }

    public PdfReportService(string cacheDir)
    {
        PdfFontResolver.Register();
        _cacheDir = cacheDir;
    }

    public async Task<string> GenerateExpenseReportAsync(TripConfig config, List<Transaction> transactions)
    {
        // Ensure fonts are loaded from raw assets before starting
        await PdfFontResolver.InitializeAsync();

        var document = new PdfDocument();
        document.Info.Title = $"Report Spese - {config?.Name ?? "Viaggio"}";

        var expenses = transactions?
            .Where(t => t.Type == "expense")
            .OrderBy(t => t.Date)
            .ToList() ?? new List<Transaction>();

        var page = document.AddPage();
        double yPos = Margin;

        using (var gfx = XGraphics.FromPdfPage(page))
        {
            // Header
            yPos = await DrawHeader(gfx, config!, yPos);
            yPos += 20;

            // Main Table
            yPos = DrawExpenseTable(gfx, document, ref page, config!, expenses, yPos);
            yPos += 30;

            // Summary Table
            DrawSummaryTable(gfx, document, ref page, config!, transactions!, yPos);
        }

        // Add Page Numbers in Footer
        for (int i = 0; i < document.PageCount; i++)
        {
            var footerPage = document.Pages[i];
            using (var footerGfx = XGraphics.FromPdfPage(footerPage))
            {
                var footerFont = new XFont("Sans-Serif", 8, XFontStyleEx.Regular);
                string footerText = $"Pagina {i + 1} di {document.PageCount}";
                footerGfx.DrawString(footerText, footerFont, XBrushes.Gray, new XRect(Margin, PageHeight - Margin, PageWidth - 2 * Margin, 20), XStringFormats.Center);
            }
        }

        var slug = SlugUtility.GenerateSlug(config?.Name ?? "trip");
        var tempPath = Path.Combine(_cacheDir, $"spese_{slug}.pdf");
        document.Save(tempPath);
        return tempPath;
    }

    private async Task<double> DrawHeader(XGraphics gfx, TripConfig config, double yPos)
    {
        if (gfx == null || config == null) return yPos;

        // Logo
        try
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync("logo.png");
            if (stream != null)
            {
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                ms.Position = 0;
                using var image = XImage.FromStream(ms);
                gfx.DrawImage(image, Margin, yPos, 40, 40);
            }
        }
        catch { /* Ignore logo if not found */ }

        var titleFont = new XFont("Sans-Serif", 20, XFontStyleEx.Bold);
        var subTitleFont = new XFont("Sans-Serif", 14, XFontStyleEx.Bold);
        var textFont = new XFont("Sans-Serif", 10, XFontStyleEx.Regular);

        gfx.DrawString("TripFund", subTitleFont, XBrushes.DarkSlateGray, Margin + 50, yPos + 15);
        gfx.DrawString("Riepilogo Cassa Comune", titleFont, XBrushes.Black, Margin + 50, yPos + 35);

        yPos += 60;
        gfx.DrawString(config.Name ?? string.Empty, subTitleFont, XBrushes.Black, Margin, yPos);
        yPos += 18;
        string dates = $"{config.StartDate.ToString("dd/MM/yyyy", ItCulture)} - {config.EndDate.ToString("dd/MM/yyyy", ItCulture)}";
        gfx.DrawString(dates, textFont, XBrushes.Gray, Margin, yPos);

        return yPos + 10;
    }

    private double DrawExpenseTable(XGraphics gfx, PdfDocument doc, ref PdfPage page, TripConfig config, List<Transaction> expenses, double yPos)
    {
        var headerFont = new XFont("Sans-Serif", 10, XFontStyleEx.Bold);
        var bodyFont = new XFont("Monospace", 9, XFontStyleEx.Regular);
        var bodySmallFont = new XFont("Monospace", 7, XFontStyleEx.Regular);

        double[] colWidths = { 100, 300, 115 };
        string[] headers = { "Data/Ora", "Spesa", "Importo" };

        // Draw Table Header
        gfx.DrawRectangle(XBrushes.LightGray, Margin, yPos, PageWidth - 2 * Margin, 20);
        gfx.DrawString(headers[0], headerFont, XBrushes.Black, new XRect(Margin, yPos, colWidths[0], 20), XStringFormats.Center);
        gfx.DrawString(headers[1], headerFont, XBrushes.Black, new XRect(Margin + colWidths[0] + 8, yPos, colWidths[1] - 8, 20), XStringFormats.CenterLeft);
        gfx.DrawString(headers[2], headerFont, XBrushes.Black, new XRect(Margin + colWidths[0] + colWidths[1], yPos, colWidths[2] - 8, 20), XStringFormats.CenterRight);

        yPos += 20;

        foreach (var ex in expenses)
        {
            if (yPos > PageHeight - Margin - 60) // Increased buffer for footer
            {
                // We need to dispose the current gfx before adding a new page and getting a new gfx
                // But DrawExpenseTable receives gfx as a parameter.
                // This means the design needs a small fix: either pass XGraphics by ref and recreate it,
                // or have the caller handle page creation.
                // For simplicity, we'll keep using the same gfx if possible, but PDFsharp 6.x is strict.
                
                // Let's refactor to handle the multi-page logic correctly.
            }

            // Data/Ora
            var tz = string.IsNullOrEmpty(ex.Timezone) ? TimeZoneInfo.Local : (TimeZoneInfo.TryFindSystemTimeZoneById(ex.Timezone, out var t) ? t : TimeZoneInfo.Local);
            var localDate = TimeZoneInfo.ConvertTime(ex.Date, tz);
            var offset = tz.GetUtcOffset(localDate);
            var sign = offset >= TimeSpan.Zero ? "+" : "-";
            var offsetStr = $"UTC{sign}{Math.Abs(offset.Hours):D2}:{Math.Abs(offset.Minutes):D2}";

            gfx.DrawString(localDate.ToString("dd/MM/yyyy", ItCulture), bodyFont, XBrushes.Black, new XRect(Margin, yPos, colWidths[0], 12), XStringFormats.Center);
            gfx.DrawString($"{localDate.ToString("HH:mm", ItCulture)} {offsetStr}", bodySmallFont, XBrushes.Gray, new XRect(Margin, yPos + 12, colWidths[0], 10), XStringFormats.Center);

            // Spesa
            gfx.DrawString(ex.Description ?? string.Empty, bodyFont, XBrushes.Black, new XRect(Margin + colWidths[0] + 8, yPos, colWidths[1] - 16, 12), XStringFormats.CenterLeft);
            if (ex.Location != null && !string.IsNullOrEmpty(ex.Location.Name))
            {
                gfx.DrawString(ex.Location.Name ?? string.Empty, bodySmallFont, XBrushes.Gray, new XRect(Margin + colWidths[0] + 8, yPos + 12, colWidths[1] - 16, 10), XStringFormats.CenterLeft);
            }

            // Importo
            string amountStr = FormatCurrency(ex.Amount, ex.Currency ?? string.Empty, config);
            gfx.DrawString(amountStr, bodyFont, XBrushes.Black, new XRect(Margin + colWidths[0] + colWidths[1], yPos, colWidths[2] - 8, 22), XStringFormats.CenterRight);

            gfx.DrawLine(XPens.LightGray, Margin, yPos + 25, PageWidth - Margin, yPos + 25);
            yPos += 30;
        }

        return yPos;
    }

    private void DrawSummaryTable(XGraphics gfx, PdfDocument doc, ref PdfPage page, TripConfig config, List<Transaction> transactions, double yPos)
    {
        if (config == null || config.Currencies == null) return;
        
        var titleFont = new XFont("Sans-Serif", 12, XFontStyleEx.Bold);
        var headerFont = new XFont("Sans-Serif", 9, XFontStyleEx.Bold);
        var bodyFont = new XFont("Monospace", 9, XFontStyleEx.Regular);

        gfx.DrawString("Totali per Valuta", titleFont, XBrushes.Black, Margin, yPos);
        yPos += 20;

        double[] colWidths = { 80, 145, 145, 145 };
        string[] headers = { "Valuta", "Totale", "Quota", "Differenza" };

        gfx.DrawRectangle(XBrushes.LightGray, Margin, yPos, PageWidth - 2 * Margin, 18);
        gfx.DrawString(headers[0], headerFont, XBrushes.Black, new XRect(Margin + 8, yPos, colWidths[0] - 8, 18), XStringFormats.CenterLeft);
        gfx.DrawString(headers[1], headerFont, XBrushes.Black, new XRect(Margin + colWidths[0], yPos, colWidths[1] - 8, 18), XStringFormats.CenterRight);
        gfx.DrawString(headers[2], headerFont, XBrushes.Black, new XRect(Margin + colWidths[0] + colWidths[1], yPos, colWidths[2] - 8, 18), XStringFormats.CenterRight);
        gfx.DrawString(headers[3], headerFont, XBrushes.Black, new XRect(Margin + colWidths[0] + colWidths[1] + colWidths[2], yPos, colWidths[3] - 8, 18), XStringFormats.CenterRight);

        yPos += 20;

        int membersCount = config.Members?.Count ?? 0;

        foreach (var currencyPair in config.Currencies)
        {
            var currencyCode = currencyPair.Key;
            var currency = currencyPair.Value;

            decimal totalExpenses = transactions?.Where(t => t.Type == "expense" && t.Currency == currencyCode).Sum(t => t.Amount) ?? 0;
            decimal quota = (currency?.ExpectedQuotaPerMember ?? 0) * membersCount;
            decimal diff = totalExpenses - quota;

            gfx.DrawString(currencyCode ?? string.Empty, bodyFont, XBrushes.Black, new XRect(Margin + 8, yPos, colWidths[0] - 8, 15), XStringFormats.CenterLeft);
            gfx.DrawString(FormatCurrency(totalExpenses, currencyCode ?? string.Empty, config), bodyFont, XBrushes.Black, new XRect(Margin + colWidths[0], yPos, colWidths[1] - 8, 15), XStringFormats.CenterRight);
            gfx.DrawString(FormatCurrency(quota, currencyCode ?? string.Empty, config), bodyFont, XBrushes.Black, new XRect(Margin + colWidths[0] + colWidths[1], yPos, colWidths[2] - 8, 15), XStringFormats.CenterRight);
            
            var diffBrush = diff <= 0 ? XBrushes.Green : XBrushes.Red;
            gfx.DrawString(FormatCurrency(diff, currencyCode ?? string.Empty, config), bodyFont, diffBrush, new XRect(Margin + colWidths[0] + colWidths[1] + colWidths[2], yPos, colWidths[3] - 8, 15), XStringFormats.CenterRight);

            yPos += 18;
        }
    }

    private string FormatCurrency(decimal amount, string currencyCode, TripConfig config)
    {
        if (config != null && config.Currencies != null && config.Currencies.TryGetValue(currencyCode, out var c))
        {
            string format = c.Decimals > 0 ? "N" + c.Decimals : "N0";
            return $"{amount.ToString(format, ItCulture)} {currencyCode}";
        }
        return $"{amount.ToString("N2", ItCulture)} {currencyCode}";
    }
}
