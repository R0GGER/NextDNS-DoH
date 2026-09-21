using System.Net.NetworkInformation;
using System.ServiceProcess;

namespace NextDnsDoh;

internal sealed class DohService : ServiceBase
{
    private const int DebounceMs = 2000;

    private readonly object _gate = new();
    private readonly object _applyLock = new();
    private System.Threading.Timer? _debounce;
    private int _stopped;

    public DohService()
    {
        ServiceName = WindowsService.ServiceName;
        CanStop = true;
        AutoLog = true;
    }

    protected override void OnStart(string[] args)
    {
        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
        ThreadPool.QueueUserWorkItem(_ => Apply());
    }

    protected override void OnStop()
    {
        Interlocked.Exchange(ref _stopped, 1);
        NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
        lock (_gate)
        {
            _debounce?.Dispose();
            _debounce = null;
        }

        // Let an in-progress apply finish so netsh is not killed halfway.
        if (Monitor.TryEnter(_applyLock, TimeSpan.FromSeconds(20)))
        {
            Monitor.Exit(_applyLock);
        }
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs e)
    {
        if (Volatile.Read(ref _stopped) != 0)
        {
            return;
        }

        try
        {
            lock (_gate)
            {
                if (Volatile.Read(ref _stopped) != 0)
                {
                    return;
                }

                _debounce ??= new System.Threading.Timer(_ => Apply(), null, Timeout.Infinite, Timeout.Infinite);
                _debounce.Change(DebounceMs, Timeout.Infinite);
            }
        }
        catch (ObjectDisposedException)
        {
            // Stop disposed the timer as this event arrived.
        }
    }

    /// <summary>
    /// Reapplies only while NextDNS is on. Turning it off must not restore DHCP
    /// on every network change; the tray toggle already did that.
    /// </summary>
    private void Apply()
    {
        if (Volatile.Read(ref _stopped) != 0)
        {
            return;
        }

        lock (_applyLock)
        {
            if (Volatile.Read(ref _stopped) != 0)
            {
                return;
            }

            var settingsPath = WindowsService.ReadSettingsPath() ?? "";
            if (string.IsNullOrWhiteSpace(settingsPath))
            {
                return;
            }

            try
            {
                var settings = AppSettings.LoadFrom(settingsPath);
                if (!settings.Enabled || !settings.HasConfigurationId)
                {
                    return;
                }

                DnsManager.Enable(settings.ConfigurationId, settings.DeviceName);
                ClearLastError(settingsPath);
            }
            catch (Exception ex)
            {
                try
                {
                    AppSettings.WriteLastErrorBeside(settingsPath, ex.Message);
                }
                catch
                {
                    // The next network change tries again.
                }
            }
        }
    }

    private static void ClearLastError(string settingsPath)
    {
        try
        {
            var directory = Path.GetDirectoryName(settingsPath);
            if (string.IsNullOrEmpty(directory))
            {
                return;
            }

            var errorPath = Path.Combine(directory, "last-error.txt");
            if (File.Exists(errorPath))
            {
                File.Delete(errorPath);
            }
        }
        catch
        {
            // A stale message is only shown if a later change also fails to write one.
        }
    }
}
