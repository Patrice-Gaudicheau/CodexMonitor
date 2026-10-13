using System.Security.Principal;
using System.Text.Json;

namespace CodexMonitor;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var verification = args.Length == 2 && args[0] == "--verify-live" ? Path.GetFullPath(args[1]) : null;
        if (args.Length != 0 && verification is null) return 2;
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        using var mutex = new Mutex(true, @"Local\CodexMonitor-" + sid, out var owned);
        if (!owned)
        {
            if (verification is not null)
            {
                Directory.CreateDirectory(verification);
                File.WriteAllText(Path.Combine(verification, "duplicate-instance.json"), JsonSerializer.Serialize(new { rejected = true, exitCode = 10 }));
            }
            return 10;
        }
        try
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            using var context = new MonitorContext(verification);
            Application.ThreadException += (_, _) => context.HandleUnexpectedFailure();
            Application.Run(context);
            return 0;
        }
        finally { mutex.ReleaseMutex(); }
    }
}
