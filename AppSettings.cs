namespace NextDnsDoh;

internal sealed class AppSettings
{
    public string ConfigurationId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public bool Enabled { get; set; }
    public bool ShowStatusBadge { get; set; } = true;
    public bool MinimalistIcon { get; set; }

    public static string DirectoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "nextdns-doh");

    public static string FilePath => Path.Combine(DirectoryPath, "settings.json");

    public static string LastErrorPath => Path.Combine(DirectoryPath, "last-error.txt");

    public static string ApplyResultPath => Path.Combine(DirectoryPath, "apply-result.txt");

    public static string ServiceResultPath => Path.Combine(DirectoryPath, "service-result.txt");

    public static AppSettings Load() => LoadFrom(FilePath);

    /// <summary>
    /// The Windows service runs as LocalSystem, so it cannot use
    /// <see cref="FilePath"/> and loads the user file stored at registration.
    /// </summary>
    public static AppSettings LoadFrom(string path)
    {
        var settings = new AppSettings();
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return settings;
            }

            var json = File.ReadAllText(path);
            settings.ConfigurationId = ReadJsonString(json, "ConfigurationId");
            settings.DeviceName = ReadJsonString(json, "DeviceName");
            settings.Enabled = ReadJsonBool(json, "Enabled");
            settings.ShowStatusBadge = ReadJsonBool(json, "ShowStatusBadge", defaultValue: true);
            settings.MinimalistIcon = ReadJsonBool(json, "MinimalistIcon");
        }
        catch
        {
            // Corrupt or unreadable settings fall back to defaults.
        }

        return settings;
    }

    public void Save()
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(FilePath,
            "{" + Environment.NewLine +
            "  \"ConfigurationId\": \"" + EscapeJson(ConfigurationId) + "\"," + Environment.NewLine +
            "  \"DeviceName\": \"" + EscapeJson(DeviceName) + "\"," + Environment.NewLine +
            "  \"Enabled\": " + (Enabled ? "true" : "false") + "," + Environment.NewLine +
            "  \"ShowStatusBadge\": " + (ShowStatusBadge ? "true" : "false") + "," + Environment.NewLine +
            "  \"MinimalistIcon\": " + (MinimalistIcon ? "true" : "false") + Environment.NewLine +
            "}");
    }

    public bool HasConfigurationId => !string.IsNullOrWhiteSpace(ConfigurationId);

    public static void WriteLastErrorBeside(string settingsFilePath, string message)
    {
        var directory = Path.GetDirectoryName(settingsFilePath);
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "last-error.txt"), message);
    }

    private static string EscapeJson(string value) =>
        (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string ReadJsonString(string json, string name)
    {
        var key = "\"" + name + "\"";
        var index = json.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return "";
        }

        var colon = json.IndexOf(':', index + key.Length);
        var start = colon < 0 ? -1 : json.IndexOf('"', colon + 1);
        var end = start < 0 ? -1 : json.IndexOf('"', start + 1);
        return start < 0 || end < 0 ? "" : json.Substring(start + 1, end - start - 1);
    }

    private static bool ReadJsonBool(string json, string name, bool defaultValue = false)
    {
        var key = "\"" + name + "\"";
        var index = json.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return defaultValue;
        }

        var colon = json.IndexOf(':', index + key.Length);
        if (colon < 0)
        {
            return false;
        }

        var rest = json.Substring(colon + 1).TrimStart();
        return rest.StartsWith("true", StringComparison.OrdinalIgnoreCase);
    }
}
