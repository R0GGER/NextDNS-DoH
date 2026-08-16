using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace NextDnsDoh;

internal static class DnsManager
{
    public const string Ipv4Primary = "45.90.28.0";
    public const string Ipv4Secondary = "45.90.30.0";

    private const ulong DnsSettingIpv6 = 0x0001;
    private const ulong DnsSettingNameServer = 0x0002;
    private const ulong DnsSettingDoh = 0x1000;
    private const ulong DnsDohServerSettingsEnable = 0x0002;

    private const uint DnsInterfaceSettingsVersion3 = 3;
    private const uint DnsServerPropertyVersion1 = 1;
    private const uint DnsServerDohProperty = 1;

    public static string BuildTemplate(string configurationId) =>
        $"https://dns.nextdns.io/{configurationId.Trim()}";

    public static bool IsEnabled()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            foreach (var address in nic.GetIPProperties().DnsAddresses)
            {
                if (address.ToString() == Ipv4Primary)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static void Enable(string configurationId)
    {
        var id = NormalizeId(configurationId);
        var template = BuildTemplate(id);
        var ipv4 = new[] { Ipv4Primary, Ipv4Secondary };
        TryBuildIpv6(id, out var ipv6Primary, out var ipv6Secondary);
        string[]? ipv6 = ipv6Primary is null ? null : [ipv6Primary, ipv6Secondary!];

        var adapters = GetTargetAdapters();
        if (adapters.Count == 0)
        {
            throw new InvalidOperationException("No active Wi-Fi or Ethernet adapter found.");
        }

        RegisterDohServers(ipv4, ipv6, template);

        var failures = new List<string>();
        foreach (var adapter in adapters)
        {
            try
            {
                ApplyOnAdapter(adapter, ipv4, ipv6, template, enable: true);
            }
            catch (Exception ex)
            {
                failures.Add($"{adapter.Name}: {ex.Message}");
            }
        }

        FlushResolverCache();

        if (failures.Count == adapters.Count)
        {
            throw new InvalidOperationException(
                "DNS could not be set on any adapter." + Environment.NewLine +
                string.Join(Environment.NewLine, failures));
        }
    }

    public static void Disable()
    {
        var adapters = GetTargetAdapters();
        TryBuildIpv6(AppSettings.Load().ConfigurationId, out var ipv6Primary, out var ipv6Secondary);
        string[] ipv4 = [Ipv4Primary, Ipv4Secondary];
        string[]? ipv6 = ipv6Primary is null ? null : [ipv6Primary, ipv6Secondary!];

        foreach (var adapter in adapters)
        {
            try
            {
                ApplyOnAdapter(adapter, ipv4, ipv6, template: null, enable: false);
            }
            catch
            {
                // Best-effort: still reset the remaining adapters.
            }
        }

        FlushResolverCache();
    }

    public static string NormalizeId(string configurationId)
    {
        var id = configurationId.Trim();
        if (id.Length is < 4 or > 16 || id.Any(c => !char.IsLetterOrDigit(c)))
        {
            throw new ArgumentException("Enter a valid NextDNS configuration ID.");
        }

        return id;
    }

    public static bool TryBuildIpv6(string configurationId, out string? primary, out string? secondary)
    {
        primary = null;
        secondary = null;
        var id = configurationId.Trim();
        if (id.Length < 6)
        {
            return false;
        }

        var prefix = id.Substring(0, 6);
        if (!prefix.All(IsHexDigit))
        {
            return false;
        }

        primary = $"2a07:a8c0::{prefix.Substring(0, 2)}:{prefix.Substring(2)}";
        secondary = $"2a07:a8c1::{prefix.Substring(0, 2)}:{prefix.Substring(2)}";
        return true;
    }

    private static bool IsHexDigit(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

    private static List<AdapterInfo> GetTargetAdapters()
    {
        var result = new List<AdapterInfo>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (!IsTargetAdapter(nic))
            {
                continue;
            }

            if (!Guid.TryParse(nic.Id, out var guid))
            {
                continue;
            }

            result.Add(new AdapterInfo(nic.Name, nic.Id, guid));
        }

        return result;
    }

    private static bool IsTargetAdapter(NetworkInterface nic)
    {
        if (nic.OperationalStatus != OperationalStatus.Up)
        {
            return false;
        }

        if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
        {
            return false;
        }

        var name = nic.Name;
        if (name.StartsWith("vEthernet", StringComparison.OrdinalIgnoreCase) ||
            name.IndexOf("Bluetooth", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return false;
        }

        if (nic.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)
        {
            return true;
        }

        return name.StartsWith("Wi-Fi", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Ethernet", StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyOnAdapter(
        AdapterInfo adapter,
        string[] ipv4,
        string[]? ipv6,
        string? template,
        bool enable)
    {
        var usedApi = TrySetInterfaceDns(adapter.Guid, ipv4, ipv6Flag: 0, template, enable);
        if (ipv6 is not null)
        {
            usedApi &= TrySetInterfaceDns(adapter.Guid, ipv6, DnsSettingIpv6, template, enable);
        }

        if (!usedApi)
        {
            SetAdapterDnsWithNetsh(adapter.Name, ipv4, ipv6, enable);
        }

        SetDohFlags(adapter.InterfaceId, ipv4, ipv6, enable);
    }

    private static bool TrySetInterfaceDns(
        Guid adapterGuid,
        IReadOnlyList<string> servers,
        ulong ipv6Flag,
        string? template,
        bool enable)
    {
        var nameServer = enable ? string.Join(",", servers) : "";
        var nameServerPtr = Marshal.StringToHGlobalUni(nameServer);
        IntPtr propertiesPtr = IntPtr.Zero;
        var allocated = new List<IntPtr>();

        try
        {
            uint propertyCount = 0;
            if (enable && template is not null)
            {
                propertiesPtr = AllocDohProperties(servers.Count, template, allocated);
                propertyCount = (uint)servers.Count;
            }

            var settings = new DnsInterfaceSettings3
            {
                Version = DnsInterfaceSettingsVersion3,
                Flags = DnsSettingNameServer | DnsSettingDoh | ipv6Flag,
                NameServer = nameServerPtr,
                ServerPropertyCount = propertyCount,
                ServerProperties = propertiesPtr
            };

            var error = NativeMethods.SetInterfaceDnsSettings(adapterGuid, ref settings);
            return error == 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(nameServerPtr);
            foreach (var ptr in allocated)
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
    }

    private static IntPtr AllocDohProperties(int count, string template, List<IntPtr> allocated)
    {
        var templatePtr = Marshal.StringToHGlobalUni(template);
        allocated.Add(templatePtr);

        var propertySize = Marshal.SizeOf<DnsServerProperty>();
        var propertiesPtr = Marshal.AllocHGlobal(propertySize * count);
        allocated.Add(propertiesPtr);

        for (var i = 0; i < count; i++)
        {
            var doh = new DnsDohServerSettings
            {
                Template = templatePtr,
                Flags = DnsDohServerSettingsEnable
            };
            var dohPtr = Marshal.AllocHGlobal(Marshal.SizeOf<DnsDohServerSettings>());
            Marshal.StructureToPtr(doh, dohPtr, false);
            allocated.Add(dohPtr);

            var property = new DnsServerProperty
            {
                Version = DnsServerPropertyVersion1,
                ServerIndex = (uint)i,
                Type = DnsServerDohProperty,
                DohSettings = dohPtr
            };
            Marshal.StructureToPtr(property, propertiesPtr + (i * propertySize), false);
        }

        return propertiesPtr;
    }

    private static void RegisterDohServers(string[] ipv4, string[]? ipv6, string template)
    {
        foreach (var server in ipv4.Concat(ipv6 ?? []))
        {
            WriteWellKnownServer(server, template);
            RunNetsh($"dns add encryption server={server} dohtemplate={template} autoupgrade=yes udpfallback=no");
            RunNetsh($"dns set encryption server={server} dohtemplate={template} autoupgrade=yes udpfallback=no");
            RunNetsh($"dnsclient add encryption server={server} dohtemplate={template} autoupgrade=yes udpfallback=no");
            RunNetsh($"dnsclient set encryption server={server} dohtemplate={template} autoupgrade=yes udpfallback=no");
        }
    }

    private static void WriteWellKnownServer(string server, string template)
    {
        using var key = Registry.LocalMachine.CreateSubKey(
            $@"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters\DohWellKnownServers\{server}");
        key.SetValue("Template", template, RegistryValueKind.String);
    }

    private static void SetDohFlags(string interfaceId, string[] ipv4, string[]? ipv6, bool enable)
    {
        var basePath = $@"SYSTEM\CurrentControlSet\Services\Dnscache\InterfaceSpecificParameters\{interfaceId}\DohInterfaceSettings";

        foreach (var server in ipv4)
        {
            SetOrDeleteDohFlag($@"{basePath}\Doh\{server}", enable);
        }

        if (ipv6 is null)
        {
            return;
        }

        foreach (var server in ipv6)
        {
            SetOrDeleteDohFlag($@"{basePath}\Doh6\{server}", enable);
        }
    }

    private static void SetOrDeleteDohFlag(string path, bool enable)
    {
        if (enable)
        {
            using var key = Registry.LocalMachine.CreateSubKey(path);
            key.SetValue("DohFlags", 1L, RegistryValueKind.QWord);
            return;
        }

        try
        {
            Registry.LocalMachine.DeleteSubKey(path, throwOnMissingSubKey: false);
        }
        catch
        {
            // Ignore missing keys.
        }
    }

    private static void SetAdapterDnsWithNetsh(string name, string[] ipv4, string[]? ipv6, bool enable)
    {
        var quoted = $"\"{name}\"";
        if (enable)
        {
            RunNetsh($"interface ip set dnsservers name={quoted} source=static address={ipv4[0]} register=none validate=no", throwOnError: true);
            RunNetsh($"interface ip add dnsservers name={quoted} address={ipv4[1]} index=2 validate=no");
            if (ipv6 is not null)
            {
                RunNetsh($"interface ipv6 set dnsservers name={quoted} source=static address={ipv6[0]} register=none validate=no");
                RunNetsh($"interface ipv6 add dnsservers name={quoted} address={ipv6[1]} index=2 validate=no");
            }
        }
        else
        {
            RunNetsh($"interface ip set dnsservers name={quoted} source=dhcp");
            RunNetsh($"interface ipv6 set dnsservers name={quoted} source=dhcp");
        }
    }

    private static void RunNetsh(string arguments, bool throwOnError = false)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "netsh",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            if (throwOnError)
            {
                throw new InvalidOperationException("netsh could not be started.");
            }

            return;
        }

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (throwOnError && process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error) ? output : error);
        }
    }

    private static void FlushResolverCache()
    {
        try
        {
            NativeMethods.DnsFlushResolverCache();
        }
        catch
        {
            // Best-effort: DNS settings were already applied.
        }
    }

    private readonly struct AdapterInfo
    {
        public AdapterInfo(string name, string interfaceId, Guid guid)
        {
            Name = name;
            InterfaceId = interfaceId;
            Guid = guid;
        }

        public string Name { get; }
        public string InterfaceId { get; }
        public Guid Guid { get; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DnsInterfaceSettings3
    {
        public uint Version;
        public ulong Flags;
        public IntPtr Domain;
        public IntPtr NameServer;
        public IntPtr SearchList;
        public uint RegistrationEnabled;
        public uint RegisterAdapterName;
        public uint EnableLlmnr;
        public uint QueryAdapterName;
        public IntPtr ProfileNameServer;
        public uint DisableUnconstrainedQueries;
        public IntPtr SupplementalSearchList;
        public uint ServerPropertyCount;
        public IntPtr ServerProperties;
        public uint ProfileServerPropertyCount;
        public IntPtr ProfileServerProperties;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DnsServerProperty
    {
        public uint Version;
        public uint ServerIndex;
        public uint Type;
        public IntPtr DohSettings;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DnsDohServerSettings
    {
        public IntPtr Template;
        public ulong Flags;
    }

    private static class NativeMethods
    {
        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        public static extern uint SetInterfaceDnsSettings(Guid guid, ref DnsInterfaceSettings3 settings);

        [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache", ExactSpelling = true)]
        public static extern uint DnsFlushResolverCache();
    }
}
