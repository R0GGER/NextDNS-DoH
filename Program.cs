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
}
