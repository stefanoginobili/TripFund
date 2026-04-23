using System.Globalization;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Microsoft.Maui.Storage;
using TripFund.App.Models;
using TripFund.App.Utilities;
using TripFund.App.Constants;

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
        }

        // Summary Tiles - ALWAYS on a new page
        var summaryPage = document.AddPage();
        using (var summaryGfx = XGraphics.FromPdfPage(summaryPage))
        {
            var titleFont = new XFont("Sans-Serif", 14, XFontStyleEx.Bold);
            summaryGfx.DrawString("Riepilogo Valute", titleFont, XBrushes.Black, Margin, Margin + 15);
            
            DrawSummaryTiles(summaryGfx, document, ref summaryPage, config!, transactions!, Margin + 35);
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
        gfx.DrawString("Spese Cassa Comune", titleFont, XBrushes.Black, Margin + 50, yPos + 35);

        yPos += 60;
        gfx.DrawString(config.Name ?? string.Empty, subTitleFont, XBrushes.Black, Margin + 50, yPos);
        yPos += 18;
        string dates = $"{config.StartDate.ToString("dd/MM/yyyy", ItCulture)} - {config.EndDate.ToString("dd/MM/yyyy", ItCulture)}";
        gfx.DrawString(dates, textFont, XBrushes.Gray, Margin + 50, yPos);

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
            if (yPos > PageHeight - Margin - 60)
            {
                // Simple multi-page logic would go here if needed
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

    private void DrawSummaryTiles(XGraphics gfx, PdfDocument doc, ref PdfPage page, TripConfig config, List<Transaction> transactions, double yPos)
    {
        if (config == null || config.Currencies == null) return;
        
        var currencyCodeFont = new XFont("Sans-Serif", 18, XFontStyleEx.Bold);
        var currencyNameFont = new XFont("Sans-Serif", 8, XFontStyleEx.Regular);
        var monoFont = new XFont("Monospace", 9, XFontStyleEx.Regular);
        var monoBoldFont = new XFont("Monospace", 9, XFontStyleEx.Bold);

        int membersCount = config.Members?.Count ?? 0;
        double spacing = 10;
        double tileWidth = (PageWidth - 2 * Margin - 2 * spacing) / 3;
        double tileHeight = 110;
        double currentX = Margin;
        int count = 0;

        // Measure monospace character width
        double charWidth = gfx.MeasureString(" ", monoFont).Width;

        foreach (var currencyPair in config.Currencies)
        {
            var currencyCode = currencyPair.Key;
            var currency = currencyPair.Value;

            decimal totalExpenses = transactions?.Where(t => t.Type == "expense" && t.Currency == currencyCode).Sum(t => t.Amount) ?? 0;
            decimal quota = (currency?.ExpectedQuotaPerMember ?? 0) * membersCount;
            decimal diff = totalExpenses - quota;

            // Draw Tile Background
            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(245, 245, 245)), currentX, yPos, tileWidth, tileHeight);
            
            // Currency Code (Top Left)
            gfx.DrawString(currencyCode ?? string.Empty, currencyCodeFont, XBrushes.DarkGray, currentX + 10, yPos + 25);

            // Currency Name (Under Code)
            var isoInfo = IsoCurrencies.All.FirstOrDefault(c => c.Code.Equals(currencyCode, System.StringComparison.OrdinalIgnoreCase));
            if (isoInfo != null)
            {
                gfx.DrawString(isoInfo.Name, currencyNameFont, XBrushes.Gray, currentX + 10, yPos + 38);
            }

            // Right-aligned values
            string totalStr = FormatAmountOnly(totalExpenses, currencyCode ?? string.Empty, config);
            string quotaStr = FormatAmountOnly(quota, currencyCode ?? string.Empty, config);
            string diffStr = FormatAmountOnly(diff, currencyCode ?? string.Empty, config);

            // Calculate max line length for alignment (with at least 3 spaces)
            int maxLen = Math.Max("Totale:".Length + totalStr.Length, 
                         Math.Max("Quota:".Length + quotaStr.Length, 
                                  "Differenza:".Length + diffStr.Length)) + 3;

            var diffBrush = diff <= 0 ? XBrushes.Green : XBrushes.Red;

            // Draw the 3-value block aligned to the bottom right
            // Adjusted offsets to avoid overlap with currency name and fit background
            DrawPaddedSummaryLine(gfx, "Totale:", totalStr, maxLen, monoBoldFont, XBrushes.Black, currentX, yPos + tileHeight - 50, tileWidth, charWidth);
            DrawPaddedSummaryLine(gfx, "Quota:", quotaStr, maxLen, monoFont, XBrushes.Gray, currentX, yPos + tileHeight - 35, tileWidth, charWidth);
            DrawPaddedSummaryLine(gfx, "Differenza:", diffStr, maxLen, monoFont, diffBrush, currentX, yPos + tileHeight - 20, tileWidth, charWidth);

            count++;
            if (count % 3 == 0)
            {
                currentX = Margin;
                yPos += tileHeight + spacing;
            }
            else
            {
                currentX += tileWidth + spacing;
            }
        }
    }

    private void DrawPaddedSummaryLine(XGraphics gfx, string label, string value, int maxLen, XFont valueFont, XBrush valueBrush, double currentX, double lineY, double tileWidth, double charWidth)
    {
        double blockWidth = maxLen * charWidth;
        double startX = currentX + tileWidth - 10 - blockWidth;
        var labelFont = new XFont("Monospace", 9, XFontStyleEx.Regular);

        // Draw Label (Gray, left-aligned in the block)
        gfx.DrawString(label, labelFont, XBrushes.Gray, new XRect(startX, lineY, blockWidth, 15), XStringFormats.CenterLeft);

        // Draw Value (Colored/Bold, right-aligned in the block)
        gfx.DrawString(value, valueFont, valueBrush, new XRect(startX, lineY, blockWidth, 15), XStringFormats.CenterRight);
    }

    private string FormatAmountOnly(decimal amount, string currencyCode, TripConfig config)
    {
        if (config != null && config.Currencies != null && config.Currencies.TryGetValue(currencyCode, out var c))
        {
            string format = c.Decimals > 0 ? "N" + c.Decimals : "N0";
            return amount.ToString(format, ItCulture);
        }
        return amount.ToString("N2", ItCulture);
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
