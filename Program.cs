using CfgUtility.Gui;
using System.Windows.Forms;

namespace CfgUtility;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            return CliEntrypoint.RunAsync(args).GetAwaiter().GetResult();
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new DeviceWorkbenchForm());
        return 0;
    }
}
