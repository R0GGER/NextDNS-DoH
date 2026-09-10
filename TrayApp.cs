using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32;

namespace NextDnsDoh;

internal sealed class TrayApp : ApplicationContext
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "NextDNS-DoH";

    private const int UpdateCheckIntervalMs = 24 * 60 * 60 * 1000;

    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _toggleItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripMenuItem _versionItem;
    private readonly SynchronizationContext _uiThread;
    private readonly System.Windows.Forms.Timer _updateTimer;
    private Icon _onIcon;
    private Icon _offIcon;
    private bool _showBadge;
    private bool _minimalist;
    private bool _lightTaskbar;
    private UpdateInfo? _availableUpdate;
    private Font? _versionItemFont;
    private bool _updateFormOpen;

    public TrayApp()
    {
        _uiThread = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        var initial = AppSettings.Load();
        _showBadge = initial.ShowStatusBadge;
        _minimalist = initial.MinimalistIcon;
        _lightTaskbar = IsLightTaskbar();
        _onIcon = TrayIcons.Create(enabled: true, _showBadge, _minimalist, _lightTaskbar);
        _offIcon = TrayIcons.Create(enabled: false, _showBadge, _minimalist, _lightTaskbar);

        _toggleItem = new ToolStripMenuItem("NextDNS on/off", null, (_, _) => Toggle());
        _startupItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleStartup());
        _versionItem = new ToolStripMenuItem($"NextDNS DoH {GetDisplayVersion()}", null, (_, _) => ShowUpdate())
        {
            Enabled = false
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(_toggleItem);
        menu.Items.Add(_startupItem);
        menu.Items.Add("Settings", null, (_, _) => EditConfigurationId());
        menu.Items.Add("My NextDNS", null, (_, _) => OpenDashboard());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_versionItem);
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        menu.Opening += (_, _) => RefreshMenu();

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Visible = true,
            Text = "NextDNS"
        };
        _notifyIcon.MouseClick += OnTrayClick;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        RefreshUi();

        _updateTimer = new System.Windows.Forms.Timer { Interval = UpdateCheckIntervalMs };
        _updateTimer.Tick += (_, _) => CheckForUpdates();
        _updateTimer.Start();
        CheckForUpdates();

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
            var message = "Changing DNS did not complete. It was cancelled, or the elevated NextDNS DoH task could not run.";
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
        using var form = new SettingsForm(
            settings.ConfigurationId,
            settings.DeviceName,
            settings.MinimalistIcon,
            settings.ShowStatusBadge);
        if (form.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        var id = DnsManager.NormalizeId(form.ConfigurationId);
        var dnsChanged = !string.Equals(settings.ConfigurationId, id, StringComparison.Ordinal)
            || !string.Equals(settings.DeviceName, form.DeviceName, StringComparison.Ordinal);
        settings.ConfigurationId = id;
        settings.DeviceName = form.DeviceName;
        settings.MinimalistIcon = form.MinimalistIcon;
        settings.ShowStatusBadge = form.ShowStatusBadge;
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
        else if (dnsChanged && (DnsManager.IsEnabled() || settings.Enabled))
        {
            Elevation.Apply(true);
            RefreshUi();
        }
    }

    private static bool EnsureConfigurationId(AppSettings settings)
    {
        if (settings.HasConfigurationId)
        {
            return true;
        }

        using var form = new SettingsForm(
            settings.ConfigurationId,
            settings.DeviceName,
            settings.MinimalistIcon,
            settings.ShowStatusBadge);
        if (form.ShowDialog() != DialogResult.OK)
        {
            return false;
        }

        settings.ConfigurationId = DnsManager.NormalizeId(form.ConfigurationId);
        settings.DeviceName = form.DeviceName;
        settings.MinimalistIcon = form.MinimalistIcon;
        settings.ShowStatusBadge = form.ShowStatusBadge;
        settings.Save();
        return true;
    }

    private void OpenDashboard()
    {
        var settings = AppSettings.Load();
        if (!EnsureConfigurationId(settings))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = $"https://my.nextdns.io/{settings.ConfigurationId}/setup",
            UseShellExecute = true
        });
    }

    private void CheckForUpdates()
    {
        var installed = GetDisplayVersion();
        _ = Task.Run(() =>
        {
            var update = UpdateChecker.Check(installed);
            if (update is null)
            {
                return;
            }

            _uiThread.Post(_ => ShowUpdateAvailable(update), null);
        });
    }

    private void ShowUpdateAvailable(UpdateInfo update)
    {
        if (_availableUpdate is not null && _availableUpdate.Version >= update.Version)
        {
            return;
        }

        _availableUpdate = update;
        _versionItem.Text = $"NextDNS DoH {GetDisplayVersion()} - update to {update.DisplayVersion}";
        var bold = new Font(_versionItem.Font, FontStyle.Bold);
        var previous = _versionItemFont;
        _versionItem.Font = bold;
        _versionItemFont = bold;
        _versionItem.Enabled = true;
        previous?.Dispose();
    }

    private void ShowUpdate()
    {
        var update = _availableUpdate;
        if (update is null || _updateFormOpen)
        {
            return;
        }

        _updateFormOpen = true;
        try
        {
            using var form = new UpdateForm(GetDisplayVersion(), update);
            form.ShowDialog();
        }
        finally
        {
            _updateFormOpen = false;
        }
    }

    private void ToggleStartup()
    {
        SetStartWithWindows(!IsStartWithWindowsEnabled());
        RefreshMenu();
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle))
        {
            return;
        }

        _uiThread.Post(_ => RefreshUi(), null);
    }

    private void RefreshUi()
    {
        var settings = AppSettings.Load();
        var enabled = DnsManager.IsEnabled() || settings.Enabled;
        ApplyIcons(enabled, settings.ShowStatusBadge, settings.MinimalistIcon);
        _notifyIcon.Text = enabled
            ? $"NextDNS: on ({settings.ConfigurationId})"
            : "NextDNS: off";
        RefreshMenu();
    }

    private void ApplyIcons(bool enabled, bool showBadge, bool minimalist)
    {
        var lightTaskbar = IsLightTaskbar();
        if (_showBadge == showBadge &&
            _minimalist == minimalist &&
            _lightTaskbar == lightTaskbar)
        {
            _notifyIcon.Icon = enabled ? _onIcon : _offIcon;
            return;
        }

        var onIcon = TrayIcons.Create(enabled: true, showBadge, minimalist, lightTaskbar);
        var offIcon = TrayIcons.Create(enabled: false, showBadge, minimalist, lightTaskbar);
        var previousOn = _onIcon;
        var previousOff = _offIcon;
        _onIcon = onIcon;
        _offIcon = offIcon;
        _showBadge = showBadge;
        _minimalist = minimalist;
        _lightTaskbar = lightTaskbar;
        _notifyIcon.Icon = enabled ? _onIcon : _offIcon;
        previousOn.Dispose();
        previousOff.Dispose();
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

    private static bool IsLightTaskbar()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int value && value == 1;
        }
        catch
        {
            return false;
        }
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
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _updateTimer.Stop();
        _updateTimer.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _onIcon.Dispose();
        _offIcon.Dispose();
        _versionItemFont?.Dispose();
        base.ExitThreadCore();
    }
}
