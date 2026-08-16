namespace NextDnsDoh;

internal sealed class AppSettings
{
    public string ConfigurationId { get; set; } = "";
    public bool Enabled { get; set; }

    public static string DirectoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "nextdns-doh");

    public static string FilePath => Path.Combine(DirectoryPath, "settings.json");

    public static string LastErrorPath => Path.Combine(DirectoryPath, "last-error.txt");

    public static AppSettings Load()
    {
        var settings = new AppSettings();
        try
        {
            if (!File.Exists(FilePath))
            {
                return settings;
            }

            var json = File.ReadAllText(FilePath);
            settings.ConfigurationId = ReadJsonString(json, "ConfigurationId");
            settings.Enabled = ReadJsonBool(json, "Enabled");
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
        var id = ConfigurationId.Replace("\\", "\\\\").Replace("\"", "\\\"");
        File.WriteAllText(FilePath,
            "{" + Environment.NewLine +
            "  \"ConfigurationId\": \"" + id + "\"," + Environment.NewLine +
            "  \"Enabled\": " + (Enabled ? "true" : "false") + Environment.NewLine +
            "}");
    }

    public bool HasConfigurationId => !string.IsNullOrWhiteSpace(ConfigurationId);

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

    private static bool ReadJsonBool(string json, string name)
    {
        var key = "\"" + name + "\"";
        var index = json.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return false;
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
