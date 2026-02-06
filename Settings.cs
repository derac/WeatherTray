using System.Text.Json;

namespace WeatherTrayApp;

public class Settings
{
    public double Latitude { get; set; } = 40.7128; // Default: New York
    public double Longitude { get; set; } = -74.0060;
    public string LocationName { get; set; } = "New York, NY";
    public bool HasBeenConfigured { get; set; } = false;

    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WeatherTrayApp", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
            }
        }
        catch { }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(this);
            File.WriteAllText(SettingsPath, json);
        }
        catch { }
    }

    /// <summary>
    /// Gets the path to the startup shortcut.
    /// </summary>
    public static string StartupShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        "WeatherTrayApp.lnk");

    /// <summary>
    /// Checks if startup is enabled.
    /// </summary>
    public static bool IsStartupEnabled => File.Exists(StartupShortcutPath);

    /// <summary>
    /// Enables or disables startup.
    /// </summary>
    public static void SetStartupEnabled(bool enabled)
    {
        try
        {
            if (enabled && !IsStartupEnabled)
            {
                // Create shortcut using Windows Script Host
                var exePath = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
                var workingDir = Path.GetDirectoryName(exePath) ?? "";
                
                // Use PowerShell to create shortcut
                var script = $@"
$ws = New-Object -ComObject WScript.Shell
$shortcut = $ws.CreateShortcut('{StartupShortcutPath.Replace("'", "''")}')
$shortcut.TargetPath = '{exePath.Replace("'", "''")}'
$shortcut.WorkingDirectory = '{workingDir.Replace("'", "''")}'
$shortcut.Save()
";
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-NoProfile -Command \"{script.Replace("\"", "\\\"")}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                System.Diagnostics.Process.Start(psi)?.WaitForExit();
            }
            else if (!enabled && IsStartupEnabled)
            {
                File.Delete(StartupShortcutPath);
            }
        }
        catch { }
    }
}
