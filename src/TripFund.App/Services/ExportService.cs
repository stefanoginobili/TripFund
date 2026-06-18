using System.IO.Compression;
using TripFund.App.Models;

namespace TripFund.App.Services;

public class ExportService : IExportService
{
    private readonly PdfReportService _pdfReportService;
    private readonly LocalStorageService _localStorageService;

    public ExportService(PdfReportService pdfReportService, LocalStorageService localStorageService)
    {
        _pdfReportService = pdfReportService;
        _localStorageService = localStorageService;
    }

    public async Task<string> GenerateExportZipAsync(string tripSlug, TripConfig config, List<Transaction> transactions)
    {
        // 1. Create a temporary directory for building the zip contents
        var tempRoot = Path.Combine(Path.GetTempPath(), $"export_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            // 2. Generate the PDF
            var pdfPath = await _pdfReportService.GenerateExpenseReportAsync(config, transactions);
            var pdfDestPath = Path.Combine(tempRoot, Path.GetFileName(pdfPath));
            File.Copy(pdfPath, pdfDestPath);

            // 3. Prepare the Allegati folder
            var allegatiDir = Path.Combine(tempRoot, "Allegati");

            // 4. Filter expenses and apply identical ordering logic to PdfReportService
            var expenses = transactions?
                .Where(t => t.Type == "expense")
                .OrderBy(t => t.Date)
                .ToList() ?? new List<Transaction>();

            var tripStorage = _localStorageService.GetLocalTripStorage(tripSlug);

            // 5. Iterate through expenses and process attachments
            int rowIndex = 1;
            bool allegatiDirCreated = false;

            foreach (var expense in expenses)
            {
                if (expense.Attachments != null && expense.Attachments.Any())
                {
                    if (!allegatiDirCreated)
                    {
                        Directory.CreateDirectory(allegatiDir);
                        allegatiDirCreated = true;
                    }

                    // Create subfolder like "001", "002" matching the PDF
                    var expenseFolder = Path.Combine(allegatiDir, rowIndex.ToString("000"));
                    Directory.CreateDirectory(expenseFolder);

                    foreach (var attachment in expense.Attachments)
                    {
                        var localAttPath = await tripStorage.GetAttachmentPath(expense.Id, attachment.Name);
                        if (!string.IsNullOrEmpty(localAttPath) && File.Exists(localAttPath))
                        {
                            var destFile = Path.Combine(expenseFolder, attachment.OriginalName);
                            // Avoid collision if there are multiple attachments with the same original name (rare but possible)
                            if (!File.Exists(destFile))
                            {
                                File.Copy(localAttPath, destFile);
                            }
                            else
                            {
                                var ext = Path.GetExtension(attachment.OriginalName);
                                var nameWithoutExt = Path.GetFileNameWithoutExtension(attachment.OriginalName);
                                var newDestFile = Path.Combine(expenseFolder, $"{nameWithoutExt}_{Guid.NewGuid().ToString().Substring(0, 4)}{ext}");
                                File.Copy(localAttPath, newDestFile);
                            }
                        }
                    }
                }
                rowIndex++;
            }

            // 6. Zip the directory
            var exportSlug = TripFund.App.Utilities.SlugUtility.GenerateSlug(config.Name ?? "trip");
            var zipPath = Path.Combine(Path.GetTempPath(), $"{exportSlug}.zip");
            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            ZipFile.CreateFromDirectory(tempRoot, zipPath, CompressionLevel.Fastest, includeBaseDirectory: false);

            return zipPath;
        }
        finally
        {
            // 7. Cleanup temp root
            if (Directory.Exists(tempRoot))
            {
                try
                {
                    Directory.Delete(tempRoot, true);
                }
                catch { /* Ignore cleanup errors in prod, OS will clean temp eventually */ }
            }
        }
    }
}
