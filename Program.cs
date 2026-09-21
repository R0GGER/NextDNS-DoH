namespace NextDnsDoh;

internal static class Program
{
    private const string MutexName = @"Local\nextdns-doh-tray";

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length >= 2 &&
            string.Equals(args[0], "--apply", StringComparison.OrdinalIgnoreCase))
        {
            var enable = args[1].Equals("on", StringComparison.OrdinalIgnoreCase);
            return Elevation.ApplyInProcess(enable);
        }

        if (args.Length >= 1 &&
            string.Equals(args[0], "--register-task", StringComparison.OrdinalIgnoreCase))
        {
            return ElevatedTask.Register(args.Length >= 2 ? args[1] : null) ? 0 : 1;
        }

        if (args.Length >= 1 &&
            string.Equals(args[0], "--unregister-task", StringComparison.OrdinalIgnoreCase))
        {
            return ElevatedTask.Unregister() ? 0 : 1;
        }

        if (args.Length >= 1 &&
            string.Equals(args[0], "--service", StringComparison.OrdinalIgnoreCase))
        {
            return WindowsService.HandleCommand(args);
        }

        if (args.Length >= 1 &&
            string.Equals(args[0], "--test-notes", StringComparison.OrdinalIgnoreCase))
        {
            return ShowTestNotes();
        }

        using var mutex = new Mutex(true, MutexName, out var created);
        if (!created)
        {
            return 0;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TrayApp());
        return 0;
    }

    private static int ShowTestNotes()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var notesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "update-notes.md");
        var notes = File.Exists(notesPath)
            ? File.ReadAllText(notesPath)
            : "No update-notes.md next to the exe.";

        var update = new UpdateInfo(
            new Version(1, 0, 9),
            "1.0.9",
            notes,
            "https://github.com/R0GGER/NextDNS-DoH/releases/download/1.0.9/NextDNS-DoH-1.0.9.exe",
            "https://github.com/R0GGER/NextDNS-DoH/releases/tag/1.0.9",
            0);

        Application.Run(new UpdateForm("1.0.8", update));
        return 0;
    }
}
