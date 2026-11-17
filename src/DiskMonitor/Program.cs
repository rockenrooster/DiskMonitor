using System.Diagnostics;
using System.Runtime.InteropServices;
using DiskMonitor.App;
using DiskMonitor.Core.Chkdsk;
using DiskMonitor.Diagnostics;
using DiskMonitor.UI.Controls;
using DiskMonitor.UI.Theming;

namespace DiskMonitor;

internal static class Program
{
    private const string MutexName = @"Local\DiskMonitor.SingleInstance";
    private static bool _errorShown;

    [STAThread]
    private static int Main(string[] args)
    {
        // 1. Helper modes that exist since the core was written.
        if (CommandLine.Has(args, "--self-check"))
        {
            return SelfCheck.Run(args);
        }

        if (CommandLine.Has(args, ChkdskHelper.HelperSwitch))
        {
            return ChkdskHelper.Run(args);
        }

        if (CommandLine.Has(args, ScanTask.OperationSwitch))
        {
            return ScanTask.RunElevatedOperation(args);
        }

        // 2. Tool modes.
        if (CommandLine.Value(args, "--make-icon") is { } iconPath)
        {
            IconArt.SaveIco(Path.GetFullPath(iconPath));
            return 0;
        }

        if (CommandLine.Has(args, ScreenRenderer.Switch))
        {
            return ScreenRenderer.Run(args);
        }

        if (CommandLine.Has(args, "--register-startup"))
        {
            return StartupManager.SetEnabled(true, AppSettings.Load().StartMinimized) ? 0 : 1;
        }

        if (CommandLine.Has(args, "--unregister-startup"))
        {
            _ = StartupManager.SetEnabled(false, false);
            return 0;
        }

        if (CommandLine.Has(args, "--unregister-shell"))
        {
            Core.Notifications.ShellRegistration.Remove();
            return 0;
        }

        // 3. Normal mode.
        string command = CommandFor(args);
        using Mutex mutex = TakeMutex(out bool owned);
        if (!owned)
        {
            _ = AllowSetForegroundWindow(-1);
            return Ipc.TrySend(command) ? 0 : 1;
        }

        if (command == "exit")
        {
            return 0;
        }

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += OnThreadException;
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ErrorLog.Write("Unhandled", e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            ErrorLog.Write("Unobserved task", e.Exception);
            e.SetObserved();
        };

        ApplicationConfiguration.Initialize();
        UI.Pages.PageRegistry.Register();
        Application.Run(new TrayApp(command, dev: CommandLine.Has(args, "--dev")));
        GC.KeepAlive(mutex);
        return 0;
    }

    /// <summary>The command to deliver: a diskmonitor: link, exit, simulate, tray, or show.</summary>
    internal static string CommandFor(string[] args)
    {
        string? link = args.FirstOrDefault(a => a.StartsWith(AppPaths.ProtocolScheme + ":", StringComparison.OrdinalIgnoreCase));
        if (link is not null)
        {
            return link;
        }

        if (CommandLine.Has(args, "--exit"))
        {
            return "exit";
        }

        if (CommandLine.Value(args, "--simulate") is { } kind)
        {
            return "simulate " + kind;
        }

        return CommandLine.Has(args, StartupManager.TraySwitch) ? "tray" : "show";
    }

    private static Mutex TakeMutex(out bool owned)
    {
        try
        {
            Mutex mutex = new(initiallyOwned: true, MutexName, out bool created);
            owned = created;
            return mutex;
        }
        catch (UnauthorizedAccessException)
        {
            // An elevated instance owns it: that counts as already running.
            owned = false;
            return new Mutex(false);
        }
        catch (AbandonedMutexException)
        {
            owned = true;
            return new Mutex(false);
        }
    }

    private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
    {
        ErrorLog.Write("UI", e.Exception);
        if (_errorShown)
        {
            return;
        }

        _errorShown = true;
        try
        {
            Form? owner = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f is UI.MainForm { Visible: true });
            if (owner is null)
            {
                return;
            }

            using KitDialog dialog = new KitDialog(Strings.ErrorDialogTitle, 400, cancelResult: 0)
                .Paragraph(Strings.ErrorDialogText)
                .Button(new DialogButton(Strings.ErrorDialogOpenLog, ButtonVariant.Secondary, 1))
                .Button(new DialogButton(Strings.ButtonClose, ButtonVariant.Primary, 0, InitialFocus: true));
            if (dialog.ShowOver(owner) == 1 && File.Exists(ErrorLog.FilePath))
            {
                using Process? _ = Process.Start(new ProcessStartInfo(ErrorLog.FilePath) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Error dialog", ex);
        }
    }

    [DllImport("user32.dll", EntryPoint = "AllowSetForegroundWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);
}
