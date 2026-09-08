using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;

namespace NextDnsDoh;

internal static class ElevatedTask
{
    private const string Folder = "NextDNS DoH";
    private const string OnTaskName = Folder + @"\Apply On";
    private const string OffTaskName = Folder + @"\Apply Off";

    private static readonly TimeSpan ApplyTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(150);

    public static bool Register(string? userName)
    {
        var principal = ResolvePrincipal(userName);
        var exe = Application.ExecutablePath;
        return Register(OnTaskName, principal, exe, "--apply on", "Turns NextDNS DNS-over-HTTPS on.")
            && Register(OffTaskName, principal, exe, "--apply off", "Turns NextDNS DNS-over-HTTPS off.");
    }

    public static bool Unregister()
    {
        var on = RunSchtasks($"/delete /tn \"{OnTaskName}\" /f") == 0;
        var off = RunSchtasks($"/delete /tn \"{OffTaskName}\" /f") == 0;
        DeleteFolder();
        return on && off;
    }

    /// <summary>
    /// Runs the registered task for <paramref name="enable"/>. Returns false when no task is
    /// available, so the caller can fall back to a UAC prompt; <paramref name="succeeded"/>
    /// carries the outcome of a task that did run.
    /// </summary>
    public static bool TryApply(bool enable, out bool succeeded)
    {
        succeeded = false;
        var taskName = enable ? OnTaskName : OffTaskName;
        if (RunSchtasks($"/query /tn \"{taskName}\"") != 0)
        {
            return false;
        }

        ClearResult();
        var startedAt = DateTime.UtcNow;
        if (RunSchtasks($"/run /tn \"{taskName}\"") != 0)
        {
            return false;
        }

        var result = WaitForResult(startedAt);
        if (result is null)
        {
            TryWriteError("Changing DNS timed out. The NextDNS DoH scheduled task did not finish.");
            return true;
        }

        succeeded = result == 0;
        return true;
    }

    private static bool Register(string taskName, string principal, string exe, string arguments, string description)
    {
        var xmlPath = Path.Combine(Path.GetTempPath(), "nextdns-doh-task.xml");
        try
        {
            // schtasks /xml expects a Unicode file.
            File.WriteAllText(xmlPath, BuildXml(taskName, principal, exe, arguments, description), Encoding.Unicode);
            return RunSchtasks($"/create /tn \"{taskName}\" /xml \"{xmlPath}\" /f") == 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            try
            {
                File.Delete(xmlPath);
            }
            catch
            {
                // Ignore cleanup failures.
            }
        }
    }

    private static string BuildXml(string taskName, string principal, string exe, string arguments, string description) =>
        $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo>
            <Author>NextDNS DoH</Author>
            <Description>{Escape(description)}</Description>
            <URI>\{Escape(taskName)}</URI>
          </RegistrationInfo>
          <Principals>
            <Principal id="Author">
              <UserId>{Escape(principal)}</UserId>
              <LogonType>InteractiveToken</LogonType>
              <RunLevel>HighestAvailable</RunLevel>
            </Principal>
          </Principals>
          <Settings>
            <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
            <AllowHardTerminate>true</AllowHardTerminate>
            <StartWhenAvailable>false</StartWhenAvailable>
            <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
            <IdleSettings>
              <StopOnIdleEnd>false</StopOnIdleEnd>
              <RestartOnIdle>false</RestartOnIdle>
            </IdleSettings>
            <AllowStartOnDemand>true</AllowStartOnDemand>
            <Enabled>true</Enabled>
            <Hidden>true</Hidden>
            <RunOnlyIfIdle>false</RunOnlyIfIdle>
            <WakeToRun>false</WakeToRun>
            <ExecutionTimeLimit>PT2M</ExecutionTimeLimit>
            <Priority>7</Priority>
          </Settings>
          <Actions Context="Author">
            <Exec>
              <Command>{Escape(exe)}</Command>
              <Arguments>{Escape(arguments)}</Arguments>
            </Exec>
          </Actions>
        </Task>
        """;

    private static string Escape(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private static string ResolvePrincipal(string? userName)
    {
        string name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            using var identity = WindowsIdentity.GetCurrent();
            name = identity.Name;
        }
        else
        {
            name = userName!.Trim();
        }

        try
        {
            return ((SecurityIdentifier)new NTAccount(name).Translate(typeof(SecurityIdentifier))).Value;
        }
        catch
        {
            return name;
        }
    }

    /// <summary>
    /// A file older than <paramref name="startedAt"/> is left over from an earlier apply that
    /// we failed to delete, so it must not be mistaken for this run's result.
    /// </summary>
    private static int? WaitForResult(DateTime startedAt)
    {
        var deadline = DateTime.UtcNow + ApplyTimeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (File.Exists(AppSettings.ApplyResultPath) &&
                    File.GetLastWriteTimeUtc(AppSettings.ApplyResultPath) >= startedAt &&
                    int.TryParse(File.ReadAllText(AppSettings.ApplyResultPath).Trim(), out var result))
                {
                    return result;
                }
            }
            catch (IOException)
            {
                // The elevated process is still writing the file.
            }

            Thread.Sleep(PollInterval);
        }

        return null;
    }

    private static void TryWriteError(string message)
    {
        try
        {
            Elevation.WriteError(message);
        }
        catch
        {
            // The tray app falls back to a generic message.
        }
    }

    private static void ClearResult()
    {
        try
        {
            if (File.Exists(AppSettings.ApplyResultPath))
            {
                File.Delete(AppSettings.ApplyResultPath);
            }
        }
        catch
        {
            // A stale file only costs us a timeout.
        }
    }

    /// <summary>
    /// schtasks cannot remove the (now empty) task folder, so ask the Task Scheduler
    /// COM object to do it. Late bound to avoid an interop dependency.
    /// </summary>
    private static void DeleteFolder()
    {
        try
        {
            var serviceType = Type.GetTypeFromProgID("Schedule.Service");
            if (serviceType is null)
            {
                return;
            }

            var service = Activator.CreateInstance(serviceType);
            serviceType.InvokeMember("Connect", BindingFlags.InvokeMethod, null, service,
                [Type.Missing, Type.Missing, Type.Missing, Type.Missing]);

            var root = serviceType.InvokeMember("GetFolder", BindingFlags.InvokeMethod, null, service, ["\\"]);
            root?.GetType().InvokeMember("DeleteFolder", BindingFlags.InvokeMethod, null, root, [Folder, 0]);
        }
        catch
        {
            // An empty leftover folder is harmless.
        }
    }

    private static int RunSchtasks(string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
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
                return -1;
            }

            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode;
        }
        catch
        {
            return -1;
        }
    }
}
