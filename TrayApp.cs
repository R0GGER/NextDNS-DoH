using System.Reflection;
using Microsoft.Win32;

namespace NextDnsDoh;

internal sealed class TrayApp : ApplicationContext
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "NextDNS-DoH";

    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _toggleItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly Icon _onIcon;
    private readonly Icon _offIcon;

    public TrayApp()
    {
        _onIcon = TrayIcons.Create(enabled: true);
        _offIcon = TrayIcons.Create(enabled: false);

        _toggleItem = new ToolStripMenuItem("NextDNS on/off", null, (_, _) => Toggle());
        _startupItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleStartup());

        var menu = new ContextMenuStrip();
        menu.Items.Add(_toggleItem);
        menu.Items.Add("Configuration ID…", null, (_, _) => EditConfigurationId());
        menu.Items.Add(_startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem($"NextDNS DoH {GetDisplayVersion()}") { Enabled = false });
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        menu.Opening += (_, _) => RefreshMenu();

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Visible = true,
            Text = "NextDNS"
        };
        _notifyIcon.MouseClick += OnTrayClick;

        RefreshUi();

        var settings = AppSettings.Load();
        if (!settings.HasConfigurationId)
        {
            // First run: ask for the ID after the message loop is running.
            var timer = new System.Windows.Forms.Timer { Interval = 200 };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                timer.Dispose();
                EditConfigurationId(firstRun: true);
            };
            timer.Start();
        }
    }

    private void OnTrayClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            Toggle();
        }
    }

    private void Toggle()
    {
        var settings = AppSettings.Load();
        var currentlyEnabled = DnsManager.IsEnabled() || settings.Enabled;
        if (!currentlyEnabled && !EnsureConfigurationId(settings))
        {
            return;
        }

        var target = !currentlyEnabled;
        var ok = Elevation.Apply(target);
        RefreshUi();

        if (!ok)
        {
            var error = Elevation.ReadLastError();
            var message = "Changing DNS was cancelled or failed. Administrator access is required.";
            if (!string.IsNullOrWhiteSpace(error))
            {
                var lines = error.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length > 0)
                {
                    message = lines[0];
                }
            }
            MessageBox.Show(message, "NextDNS DoH", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void EditConfigurationId(bool firstRun = false)
    {
        var settings = AppSettings.Load();
        using var form = new SettingsForm(settings.ConfigurationId);
        if (form.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        settings.ConfigurationId = DnsManager.NormalizeId(form.ConfigurationId);
        settings.Save();
        RefreshUi();

        if (firstRun)
        {
            var enableNow = MessageBox.Show(
                "Enable NextDNS now?",
                "NextDNS DoH",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) == DialogResult.Yes;
            if (enableNow)
            {
                Toggle();
            }
        }
    }

    private static bool EnsureConfigurationId(AppSettings settings)
    {
        if (settings.HasConfigurationId)
        {
            return true;
        }

        using var form = new SettingsForm(settings.ConfigurationId);
        if (form.ShowDialog() != DialogResult.OK)
        {
            return false;
        }

        settings.ConfigurationId = DnsManager.NormalizeId(form.ConfigurationId);
        settings.Save();
        return true;
    }

    private void ToggleStartup()
    {
        SetStartWithWindows(!IsStartWithWindowsEnabled());
        RefreshMenu();
    }

    private void RefreshUi()
    {
        var settings = AppSettings.Load();
        var enabled = DnsManager.IsEnabled() || settings.Enabled;
        _notifyIcon.Icon = enabled ? _onIcon : _offIcon;
        _notifyIcon.Text = enabled
            ? $"NextDNS: on ({settings.ConfigurationId})"
            : "NextDNS: off";
        RefreshMenu();
    }

    private void RefreshMenu()
    {
        var settings = AppSettings.Load();
        var enabled = DnsManager.IsEnabled() || settings.Enabled;
        _toggleItem.Checked = enabled;
        _toggleItem.Text = enabled ? "NextDNS: on" : "NextDNS: off";
        _startupItem.Checked = IsStartWithWindowsEnabled();
    }

    private static string GetDisplayVersion()
    {
        var informational = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            return informational.Split('+')[0];
        }

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "1.0.0" : version.ToString(3);
    }

    private static bool IsStartWithWindowsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(RunValueName) is string;
    }

    private static void SetStartWithWindows(bool enable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enable)
        {
            var exe = Application.ExecutablePath;
            key.SetValue(RunValueName, $"\"{exe}\"");
        }
        else
        {
            key.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
    }

    protected override void ExitThreadCore()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _onIcon.Dispose();
        _offIcon.Dispose();
        base.ExitThreadCore();
    }
}
