using DiskMonitor.Core.Chkdsk;
using DiskMonitor.Diagnostics;

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

        return 0;
    }
}
