using DiskMonitor.Core.Chkdsk;
using DiskMonitor.Diagnostics;
using DiskMonitor.UI.Theming;

namespace DiskMonitor;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
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

        if (CommandLine.Value(args, "--make-icon") is { } iconPath)
        {
            IconArt.SaveIco(Path.GetFullPath(iconPath));
            return 0;
        }

        if (CommandLine.Has(args, ScreenRenderer.Switch))
        {
            return ScreenRenderer.Run(args);
        }

        return 0;
    }
}
