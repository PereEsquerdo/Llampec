using System.Diagnostics;

namespace Llampec.Diagnostics;

/// <summary>
/// Minimal logging: debugger output in Debug builds; a small rolling text file only when the user enables it in
/// settings (see <c>AppSettings.EnableLogFile</c>). Nothing ever leaves the machine.
/// </summary>
public static class Log
{
    private static readonly object Gate = new();
    private static string? _filePath;

    /// <summary>Enables writing to <paramref name="filePath"/>. Pass null to disable.</summary>
    public static void SetFile(string? filePath)
    {
        lock (Gate)
        {
            _filePath = filePath;
        }

        // Logging must also work on a fresh installation before settings have
        // created the application directory.
        if (filePath is not null)
        {
            try
            {
                string? dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    private static void Write(string level, string message)
    {
#if DEBUG
        Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}");
#endif
        // Without a log file (the default) nothing is formatted or locked.
        if (Volatile.Read(ref _filePath) is null) return;
        try
        {
            lock (Gate)
            {
                if (_filePath is not { } path) return;
                var info = new FileInfo(path);
                if (info.Exists && info.Length > 512 * 1024)
                {
                    File.Delete(path);
                }

                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
            // Logging must never break the app.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
