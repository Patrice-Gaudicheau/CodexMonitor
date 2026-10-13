using System.Security.Principal;
using System.Text.Json;
using CodexMonitor.Core;

namespace CodexMonitor;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Diagnostic override affects this process only; normal startup always uses Windows UI culture.
        if (args.Length == 3 && args[0] == "--verify-localization")
        {
            try
            {
                System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(args[1]);
                Localization.Initialize();
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                LocalizationVerification.Run(Path.GetFullPath(args[2]));
                return 0;
            }
            catch (Exception error)
            {
                Directory.CreateDirectory(Path.GetFullPath(args[2]));
                File.WriteAllText(Path.Combine(Path.GetFullPath(args[2]), "failure.txt"), error.ToString());
                return 1;
            }
        }
        var verifyLanguageSwitch = args.Length == 4 && args[3] == "--switch-language";
        var verification = (args.Length is 2 or 3 || verifyLanguageSwitch) && args[0] == "--verify-live" ? Path.GetFullPath(args[1]) : null;
        if (verification is not null && args.Length >= 3)
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(args[2]);
        Localization.Initialize(verification is null ? Settings.Load().PreferredLanguage : LanguagePreference.Automatic);
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
            using var context = new MonitorContext(verification, verifyLanguageSwitch);
            Application.ThreadException += (_, _) => context.HandleUnexpectedFailure();
            Application.Run(context);
            return 0;
        }
        finally { mutex.ReleaseMutex(); }
    }
}
