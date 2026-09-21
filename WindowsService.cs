using System.Diagnostics;
using System.ServiceProcess;
using Microsoft.Win32;

namespace NextDnsDoh;

internal static class WindowsService
{
    public const string ServiceName = "NextDNSDoH";
    public const string DisplayName = "NextDNS DoH";

    private const string RegistryPath = @"SOFTWARE\NextDNS DoH";
    private const string SettingsPathValue = "SettingsPath";
    private const string Description =
        "Applies NextDNS DNS-over-HTTPS at startup and when a network connects.";

    private const int ErrorServiceDoesNotExist = 1060;
    private const int ErrorServiceMarkedForDelete = 1072;
    private const int ErrorServiceNotActive = 1062;

    private static readonly TimeSpan ServiceWait = TimeSpan.FromSeconds(30);

    public static int HandleCommand(string[] args)
    {
        if (args.Length >= 2 && args[1].Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            return RunControl(install: true);
        }

        if (args.Length >= 2 && args[1].Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            return RunControl(install: false);
        }

        if (args.Length == 1)
        {
            try
            {
                ServiceBase.Run(new DohService());
                return 0;
            }
            catch (Exception ex)
            {
                Elevation.WriteError(ex.Message);
                return 1;
            }
        }

        return 1;
    }

    public static bool IsInstalled()
    {
        try
        {
            using var controller = new ServiceController(ServiceName);
            _ = controller.Status;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsRunning()
    {
        try
        {
            using var controller = new ServiceController(ServiceName);
            return controller.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Installs or removes the service. Uses the scheduled task when it exists,
    /// otherwise a UAC prompt, matching the DNS toggle.
    /// </summary>
    public static bool SetEnabled(bool enable)
    {
        if (IsDesiredState(enable))
        {
            return true;
        }

        // A LocalSystem service that starts an exe from a user-writable folder
        // would be a privilege escalation, the same reason the elevated tasks
        // only point at the Program Files copy.
        if (enable && !IsUnderProgramFiles(Application.ExecutablePath))
        {
            Elevation.WriteError("Install NextDNS DoH with the installer before running it as a service.");
            return false;
        }

        Elevation.ClearError();

        if (Elevation.IsElevated)
        {
            return enable ? Install() : Uninstall();
        }

        if (ElevatedTask.TryService(enable, out var succeeded))
        {
            return succeeded;
        }

        return Elevation.RunElevated(enable ? "--service on" : "--service off");
    }

    public static string? ReadSettingsPath()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RegistryPath);
            var value = key?.GetValue(SettingsPathValue) as string;
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }

    private static int RunControl(bool install)
    {
        try
        {
            var ok = install ? Install() : Uninstall();
            WriteResult(ok ? 0 : 1);
            return ok ? 0 : 1;
        }
        catch (Exception ex)
        {
            try
            {
                Elevation.WriteError(ex.Message);
            }
            catch
            {
                // The result file still tells the tray the attempt failed.
            }

            WriteResult(1);
            return 1;
        }
    }

    private static bool Install()
    {
        var exe = Application.ExecutablePath;
        if (!IsUnderProgramFiles(exe))
        {
            Elevation.WriteError("Install NextDNS DoH with the installer before running it as a service.");
            return false;
        }

        if (!WriteSettingsPath())
        {
            return false;
        }

        if (!IsInstalled())
        {
            var create = $"create {ServiceName} binPath= {QuoteBinPath(exe)} start= auto DisplayName= \"{DisplayName}\" obj= LocalSystem";
            var code = RunSc(create, out var output);
            if (code == ErrorServiceMarkedForDelete)
            {
                Thread.Sleep(1000);
                code = RunSc(create, out output);
            }

            if (code != 0)
            {
                WriteScError(output, "The NextDNS DoH service could not be created.");
                return false;
            }
        }

        var config = $"config {ServiceName} binPath= {QuoteBinPath(exe)} start= auto DisplayName= \"{DisplayName}\" depend= Dnscache";
        if (RunSc(config, out var configOutput) != 0)
        {
            WriteScError(configOutput, "The NextDNS DoH service could not be configured.");
            return false;
        }

        RunSc($"description {ServiceName} \"{Description}\"", out _);
        return Start();
    }

    private static bool Uninstall()
    {
        if (!TryStop())
        {
            return false;
        }

        if (!IsInstalled())
        {
            DeleteSettingsKey();
            return true;
        }

        var code = RunSc($"delete {ServiceName}", out var output);
        if (code != 0 && code != ErrorServiceDoesNotExist && code != ErrorServiceMarkedForDelete)
        {
            WriteScError(output, "The NextDNS DoH service could not be removed.");
            return false;
        }

        DeleteSettingsKey();
        return true;
    }

    private static bool Start()
    {
        try
        {
            using var controller = new ServiceController(ServiceName);
            controller.Refresh();
            if (controller.Status is not (ServiceControllerStatus.Running or ServiceControllerStatus.StartPending))
            {
                var code = RunSc($"start {ServiceName}", out var output);
                controller.Refresh();
                if (code != 0 &&
                    controller.Status is not (ServiceControllerStatus.Running or ServiceControllerStatus.StartPending))
                {
                    WriteScError(output, "The NextDNS DoH service could not be started.");
                    return false;
                }
            }

            controller.WaitForStatus(ServiceControllerStatus.Running, ServiceWait);
            return true;
        }
        catch (System.ServiceProcess.TimeoutException)
        {
            Elevation.WriteError("The NextDNS DoH service did not start in time.");
            return false;
        }
        catch (Exception ex)
        {
            Elevation.WriteError(ex.Message);
            return false;
        }
    }

    private static bool TryStop()
    {
        ServiceController? controller = null;
        try
        {
            controller = new ServiceController(ServiceName);
            controller.Refresh();
            if (controller.Status != ServiceControllerStatus.Stopped)
            {
                var code = RunSc($"stop {ServiceName}", out var output);
                controller.Refresh();
                if (code != 0 &&
                    code != ErrorServiceNotActive &&
                    controller.Status is not (ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending))
                {
                    WriteScError(output, "The NextDNS DoH service could not be stopped.");
                    return false;
                }
            }

            controller.WaitForStatus(ServiceControllerStatus.Stopped, ServiceWait);
            return true;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
        catch (System.ServiceProcess.TimeoutException)
        {
            Elevation.WriteError("The NextDNS DoH service did not stop in time.");
            return false;
        }
        catch (Exception ex)
        {
            Elevation.WriteError(ex.Message);
            return false;
        }
        finally
        {
            controller?.Dispose();
        }
    }

    private static bool IsDesiredState(bool enable)
    {
        if (!enable)
        {
            return !IsInstalled();
        }

        return IsRunning();
    }

    private static bool IsUnderProgramFiles(string exe)
    {
        try
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (string.IsNullOrWhiteSpace(programFiles))
            {
                return false;
            }

            var root = Path.GetFullPath(programFiles)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return Path.GetFullPath(exe).StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// sc.exe expects the service command line, including its own quotes, as one
    /// binPath argument: binPath= "\"C:\Program Files\NextDNS DoH\nextdns-doh.exe\" --service"
    /// </summary>
    private static string QuoteBinPath(string exe) =>
        "\"\\\"" + exe + "\\\" --service\"";

    private static bool WriteSettingsPath()
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(RegistryPath);
            if (key is null)
            {
                Elevation.WriteError("The NextDNS DoH service settings could not be saved.");
                return false;
            }

            key.SetValue(SettingsPathValue, AppSettings.FilePath, RegistryValueKind.String);
            return true;
        }
        catch (Exception ex)
        {
            Elevation.WriteError(ex.Message);
            return false;
        }
    }

    private static void DeleteSettingsKey()
    {
        try
        {
            Registry.LocalMachine.DeleteSubKeyTree(RegistryPath, throwOnMissingSubKey: false);
        }
        catch
        {
            // A leftover key only keeps a settings path.
        }
    }

    private static void WriteScError(string output, string fallback)
    {
        Elevation.WriteError(string.IsNullOrWhiteSpace(output) ? fallback : FirstLine(output));
    }

    private static string FirstLine(string text)
    {
        foreach (var line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
            {
                return trimmed;
            }
        }

        return text.Trim();
    }

    private static void WriteResult(int result)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.DirectoryPath);
            File.WriteAllText(AppSettings.ServiceResultPath, result.ToString());
        }
        catch
        {
            // The caller falls back to a timeout.
        }
    }

    private static int RunSc(string arguments, out string output)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                output = "";
                return -1;
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            output = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            if (!string.IsNullOrWhiteSpace(stdout) && !string.IsNullOrWhiteSpace(stderr))
            {
                output = stdout + Environment.NewLine + stderr;
            }

            return process.ExitCode;
        }
        catch (Exception ex)
        {
            output = ex.Message;
            return -1;
        }
    }
}
