namespace CfgUtility.Services;

public sealed class AppPaths
{
    public AppPaths(string? baseDirectory = null)
    {
        BaseDirectory = Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory);
        FilesDirectory = Path.Combine(BaseDirectory, "files");
        SwapDirectory = Path.Combine(FilesDirectory, "swp");
        IrecoveryPath = ResolveTool("irecovery.exe", "irecovery");
        UsbLiter8Directory = Path.Combine(FilesDirectory, "usbliter8");
        UsbLiter8BootPath = ResolveUsbLiter8BootTool();
    }

    public string BaseDirectory { get; }
    public string FilesDirectory { get; }
    public string SwapDirectory { get; }
    public string IrecoveryPath { get; }
    public string UsbLiter8Directory { get; }
    public string UsbLiter8BootPath { get; }

    public string DriversDirectory => Path.Combine(FilesDirectory, "drivers");

    private string ResolveUsbLiter8BootTool()
    {
        var candidates = new[]
        {
            "usbliter8_boot_new.exe",
            "usbliter8_boot.exe",
            "usbliter8_boot",
            "usbliter8.exe"
        };

        foreach (var candidate in candidates)
        {
            var path = Path.Combine(UsbLiter8Directory, candidate);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return Path.Combine(UsbLiter8Directory, candidates[0]);
    }

    private string ResolveTool(string windowsName, string unixName)
    {
        var localWindows = Path.Combine(FilesDirectory, windowsName);
        if (File.Exists(localWindows))
        {
            return localWindows;
        }

        return OperatingSystem.IsWindows() ? windowsName : unixName;
    }
}
