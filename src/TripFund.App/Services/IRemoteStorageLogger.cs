using System.Text;

namespace TripFund.App.Services;

public interface IRemoteStorageLogger
{
    string? CurrentFolderName { get; set; }
    void LogApiCall(string method, string url, string description);
    void LogInfo(string message);
    void LogWarning(string message);
    void LogError(string message, Exception? ex = null);
    string GetLogContent();
    void Clear();
}

public class RemoteStorageLogger : IRemoteStorageLogger
{
    private readonly StringBuilder _log = new();
    private readonly object _lock = new();

    public string? CurrentFolderName { get; set; }

    public void LogApiCall(string method, string url, string description)
    {
        lock (_lock)
        {
            var prefix = string.IsNullOrEmpty(CurrentFolderName) ? "" : $"[{CurrentFolderName}] ";
            _log.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] API CALL: {method} {url}");
            _log.AppendLine($"             Description: {prefix}{description}");
            _log.AppendLine();
        }
    }

    public void LogInfo(string message)
    {
        lock (_lock)
        {
            var prefix = string.IsNullOrEmpty(CurrentFolderName) ? "" : $"[{CurrentFolderName}] ";
            _log.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] INFO: {prefix}{message}");
        }
    }

    public void LogWarning(string message)
    {
        lock (_lock)
        {
            var prefix = string.IsNullOrEmpty(CurrentFolderName) ? "" : $"[{CurrentFolderName}] ";
            _log.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] WARNING: {prefix}{message}");
        }
    }

    public void LogError(string message, Exception? ex = null)
    {
        lock (_lock)
        {
            var prefix = string.IsNullOrEmpty(CurrentFolderName) ? "" : $"[{CurrentFolderName}] ";
            _log.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR: {prefix}{message}");
            if (ex != null)
            {
                _log.AppendLine($"            Exception: {ex.Message}");
                _log.AppendLine($"            Stack Trace: {ex.StackTrace}");
            }
            _log.AppendLine();
        }
    }

    public string GetLogContent()
    {
        lock (_lock)
        {
            return _log.ToString();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _log.Clear();
        }
    }
}
