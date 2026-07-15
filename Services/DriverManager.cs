using System.Runtime.InteropServices;

namespace CfgUtility.Services;

public sealed class DriverManager
{
    private readonly AppPaths _paths;
    private readonly ProcessRunner _runner;
    private readonly Action<string>? _log;

    public DriverManager(AppPaths paths, ProcessRunner runner, Action<string>? log = null)
    {
        _paths = paths;
        _runner = runner;
        _log = log;
    }

    public async Task<bool> EnsureAppleDriverAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            _log?.Invoke("Apple driver install is a Windows-only operation.");
            return false;
        }

        var driverRoot = Path.Combine(_paths.DriversDirectory, "usb", Environment.Is64BitOperatingSystem ? "x64" : "x86");
        var inf = Path.Combine(driverRoot, Environment.Is64BitOperatingSystem ? "usbaapl64.inf" : "usbaapl.inf");
        if (!File.Exists(inf))
        {
            _log?.Invoke($"WARNING: Apple driver INF not found at {inf}");
            return false;
        }

        var dpinst = Path.Combine(driverRoot, "dpinst64.exe");
        if (File.Exists(dpinst))
        {
            _log?.Invoke("Installing Apple USB driver via dpinst...");
            var result = await _runner.RunAsync(dpinst, ["/F", "/LM", "/S"], driverRoot, TimeSpan.FromSeconds(30), cancellationToken, _log).ConfigureAwait(false);
            return result.ExitCode == 0;
        }

        _log?.Invoke("Installing Apple USB driver via pnputil...");
        var pnputil = await _runner.RunAsync(
            "pnputil.exe",
            ["/add-driver", inf, "/install"],
            driverRoot,
            TimeSpan.FromSeconds(30),
            cancellationToken,
            _log).ConfigureAwait(false);
        return pnputil.ExitCode == 0;
    }

    public async Task<bool> InstallLibUsbKForDfuAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            _log?.Invoke("libusbK driver binding is a Windows-only operation.");
            return false;
        }

        _log?.Invoke("Switching DFU device to libusbK driver...");
        var libusbRoot = Path.Combine(_paths.DriversDirectory, "libusbK");
        var inf = Path.Combine(libusbRoot, "Apple_Mobile_Device_(DFU_Mode).inf");
        var dpscat = Path.Combine(libusbRoot, "dpscat.exe");
        if (!File.Exists(inf))
        {
            _log?.Invoke($"ERROR: libusbK INF not found at: {inf}");
            return false;
        }

        if (File.Exists(dpscat))
        {
            await _runner.RunAsync(dpscat, [], libusbRoot, TimeSpan.FromSeconds(30), cancellationToken, _log).ConfigureAwait(false);
        }

        await RemoveMatchingDriverPackagesAsync("libusbK", libusbRoot, cancellationToken).ConfigureAwait(false);
        var add = await _runner.RunAsync(
            "pnputil.exe",
            ["/add-driver", inf, "/install"],
            libusbRoot,
            TimeSpan.FromSeconds(30),
            cancellationToken,
            _log).ConfigureAwait(false);

        if (add.ExitCode != 0)
        {
            return false;
        }

        var dpinst = Path.Combine(libusbRoot, "dpinst64.exe");
        if (File.Exists(dpinst))
        {
            await _runner.RunAsync(dpinst, ["/F", "/LM", "/S"], libusbRoot, TimeSpan.FromSeconds(30), cancellationToken, _log).ConfigureAwait(false);
        }

        ForceBindLibUsbK(inf);
        await _runner.RunAsync("pnputil.exe", ["/scan-devices"], libusbRoot, TimeSpan.FromSeconds(30), cancellationToken, _log).ConfigureAwait(false);
        return true;
    }

    public async Task FixAppleUsbDriverAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            _log?.Invoke("Driver fix is Windows-only.");
            return;
        }

        var windowsInf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "inf");
        _log?.Invoke("[FixDriver] [1/4] Scanning installed drivers...");
        var oldPackages = Directory.Exists(windowsInf)
            ? Directory.GetFiles(windowsInf, "oem*.inf").Where(IsAppleUsbInf).ToArray()
            : [];

        _log?.Invoke($"[FixDriver] Found {oldPackages.Length} existing driver package(s).");
        _log?.Invoke("[FixDriver] [2/4] Removing old drivers...");
        foreach (var package in oldPackages)
        {
            await _runner.RunAsync(
                "pnputil.exe",
                ["/delete-driver", package, "/uninstall", "/force"],
                windowsInf,
                TimeSpan.FromSeconds(30),
                cancellationToken,
                line => _log?.Invoke("[FixDriver] " + line)).ConfigureAwait(false);
        }

        _log?.Invoke("[FixDriver] [3/4] Installing Apple USB driver...");
        await EnsureAppleDriverAsync(cancellationToken).ConfigureAwait(false);
        _log?.Invoke("[FixDriver] [4/4] Scanning for devices and restarting...");
        await _runner.RunAsync("pnputil.exe", ["/scan-devices"], _paths.FilesDirectory, TimeSpan.FromSeconds(30), cancellationToken, _log).ConfigureAwait(false);
        _log?.Invoke("[FixDriver] Done. Driver fix complete.");
    }

    private async Task RemoveMatchingDriverPackagesAsync(string provider, string workingDirectory, CancellationToken cancellationToken)
    {
        var enumResult = await _runner.RunAsync(
            "pnputil.exe",
            ["/enum-drivers"],
            workingDirectory,
            TimeSpan.FromSeconds(30),
            cancellationToken).ConfigureAwait(false);

        foreach (var package in ExtractPublishedNames(enumResult.CombinedOutput, provider))
        {
            _log?.Invoke($"Removing package: {package}");
            await _runner.RunAsync(
                "pnputil.exe",
                ["/delete-driver", package, "/uninstall", "/force"],
                workingDirectory,
                TimeSpan.FromSeconds(30),
                cancellationToken,
                _log).ConfigureAwait(false);
        }
    }

    private static IEnumerable<string> ExtractPublishedNames(string pnputilOutput, string provider)
    {
        string? current = null;
        var providerMatch = false;
        foreach (var line in pnputilOutput.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("Published Name", StringComparison.OrdinalIgnoreCase))
            {
                current = trimmed.Split(':', 2).Last().Trim();
                providerMatch = false;
            }
            else if (trimmed.Contains(provider, StringComparison.OrdinalIgnoreCase))
            {
                providerMatch = true;
            }
            else if (trimmed.Length == 0 && current != null)
            {
                if (providerMatch)
                {
                    yield return current;
                }

                current = null;
                providerMatch = false;
            }
        }

        if (current != null && providerMatch)
        {
            yield return current;
        }
    }

    private static bool IsAppleUsbInf(string path)
    {
        try
        {
            var text = File.ReadAllText(path);
            return text.Contains("USBAAPL64", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("usbaapl64", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("AppleUSB", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("Apple_Mobile_Device_(DFU_Mode)", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void ForceBindLibUsbK(string infPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (!UpdateDriverForPlugAndPlayDevices(
                IntPtr.Zero,
                "USB\\VID_05AC&PID_1227",
                Path.GetDirectoryName(infPath) ?? infPath,
                5,
                out var rebootRequired))
        {
            throw new InvalidOperationException("Windows could not bind libusbK: " + Marshal.GetLastWin32Error());
        }
    }

    [DllImport("newdev.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UpdateDriverForPlugAndPlayDevices(
        IntPtr hwndParent,
        string hardwareId,
        string fullInfPath,
        uint installFlags,
        out bool rebootRequired);
}
