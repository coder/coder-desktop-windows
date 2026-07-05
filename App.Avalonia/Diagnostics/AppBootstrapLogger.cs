using System.Text;

namespace Coder.Desktop.App.Diagnostics;

internal static class AppBootstrapLogger
{
    private static readonly object FileLock = new();

    public static string LogFilePath { get; } = ResolveLogFilePath();

    public static void Info(string message)
    {
        Write("INF", message, null);
    }

    public static void Warn(string message)
    {
        Write("WRN", message, null);
    }

    public static void Error(string message, Exception? exception)
    {
        Write("ERR", message, exception);
    }

    private static void Write(string level, string message, Exception? exception)
    {
        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz");
        var line = $"[{timestamp}] [{level}] {message}";

        try
        {
            Console.Error.WriteLine(line);
            if (exception != null)
                Console.Error.WriteLine(exception);
        }
        catch
        {
            // best effort
        }

        try
        {
            var builder = new StringBuilder();
            builder.AppendLine(line);
            if (exception != null)
                builder.AppendLine(exception.ToString());

            lock (FileLock)
            {
                File.AppendAllText(LogFilePath, builder.ToString());
            }
        }
        catch
        {
            // best effort
        }
    }

    private static string ResolveLogFilePath()
    {
        try
        {
            var stateHome = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
            if (string.IsNullOrWhiteSpace(stateHome))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                stateHome = string.IsNullOrWhiteSpace(home)
                    ? Path.GetTempPath()
                    : Path.Combine(home, ".local", "state");
            }

            var directory = Path.Combine(stateHome, "coder-desktop");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, "app-avalonia.log");
        }
        catch
        {
            return Path.Combine(Path.GetTempPath(), "coder-desktop-app-avalonia.log");
        }
    }
}
