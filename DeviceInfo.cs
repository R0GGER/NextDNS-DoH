using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace NextDnsDoh;

internal static class DeviceInfo
{
    private const string AppId = "NextDNS";

    public static string GetId()
    {
        var source = ReadMachineGuid();
        if (string.IsNullOrWhiteSpace(source))
        {
            source = Environment.MachineName;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(source));
        var hex = ToHex(hmac.ComputeHash(Encoding.UTF8.GetBytes(AppId)));
        if (hex.Length > 5)
        {
            hex = hex.Substring(0, 5);
        }

        return hex.ToUpperInvariant();
    }

    public static string GetName()
    {
        try
        {
            var name = Environment.MachineName.Trim();
            return string.IsNullOrEmpty(name) ? "" : name;
        }
        catch
        {
            return "";
        }
    }

    public static string GetModel()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            if (key is null)
            {
                return "";
            }

            var product = CleanModelPart(key.GetValue("SystemProductName") as string);
            var manufacturer = CleanModelPart(key.GetValue("SystemManufacturer") as string);
            if (product.Length == 0)
            {
                return Sanitize(manufacturer);
            }

            if (manufacturer.Length == 0 ||
                product.IndexOf(manufacturer, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return Sanitize(product);
            }

            return Sanitize(manufacturer + " " + product);
        }
        catch
        {
            return "";
        }
    }

    public static string ResolveName(string? configuredName)
    {
        var name = configuredName?.Trim() ?? "";
        if (name.Length == 0)
        {
            name = GetName();
        }

        return Sanitize(name);
    }

    private static string ReadMachineGuid()
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return (key?.GetValue("MachineGuid") as string)?.Trim() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static string CleanModelPart(string? value)
    {
        var text = value?.Trim() ?? "";
        if (text.Length == 0)
        {
            return "";
        }

        if (text.Equals("System Product Name", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("System manufacturer", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("To be filled by O.E.M.", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("Default string", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        return text;
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (!char.IsControl(c) && c != '"')
            {
                builder.Append(c);
            }
        }

        var cleaned = builder.ToString().Trim();
        return cleaned.Length <= 64 ? cleaned : cleaned.Substring(0, 64).Trim();
    }

    private static string ToHex(byte[] bytes)
    {
        var builder = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            builder.Append(b.ToString("x2"));
        }

        return builder.ToString();
    }
}
