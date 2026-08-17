using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows.Forms;

namespace NextDnsDoh;

internal static class Elevation
{
    public static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public static bool Apply(bool enable)
    {
        if (IsElevated)
        {
            return ApplyInProcess(enable) == 0;
        }

        return RunElevated(enable ? "--apply on" : "--apply off");
    }

    public static int ApplyInProcess(bool enable)
    {
        try
        {
            var settings = AppSettings.Load();
            if (enable)
            {
                if (!settings.HasConfigurationId)
                {
                    WriteError("No NextDNS configuration ID is set.");
                    return 2;
                }

                DnsManager.Enable(settings.ConfigurationId, settings.DeviceName);
            }
            else
            {
                DnsManager.Disable();
            }

            settings.Enabled = enable;
            settings.Save();
            ClearError();
            return 0;
        }
        catch (Exception ex)
        {
            WriteError(ex.Message + Environment.NewLine + ex);
            return 1;
        }
    }

    public static string? ReadLastError()
    {
        try
        {
            return File.Exists(AppSettings.LastErrorPath)
                ? File.ReadAllText(AppSettings.LastErrorPath)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool RunElevated(string arguments)
    {
        var exe = Application.ExecutablePath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            return false;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas"
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            // UAC declined.
            return false;
        }
    }

    private static void WriteError(string message)
    {
        Directory.CreateDirectory(AppSettings.DirectoryPath);
        File.WriteAllText(AppSettings.LastErrorPath, message);
    }

    private static void ClearError()
    {
        try
        {
            if (File.Exists(AppSettings.LastErrorPath))
            {
                File.Delete(AppSettings.LastErrorPath);
            }
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }
}
