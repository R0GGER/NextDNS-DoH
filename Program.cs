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
