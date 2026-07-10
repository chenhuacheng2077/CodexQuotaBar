namespace CodexQuotaBar.Services;

public static class AppLog
{
    public static void Write(string message)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexQuotaBar", "logs");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, $"{DateTime.Today:yyyy-MM-dd}.log"), $"{DateTimeOffset.Now:u} {message}{Environment.NewLine}");
        }
        catch { }
    }
}
