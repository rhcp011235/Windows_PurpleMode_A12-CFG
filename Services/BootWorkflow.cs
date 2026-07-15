using System.IO.Compression;
using System.Net.Http;

namespace CfgUtility.Services;

public sealed class BootWorkflow
{
    private static readonly HttpClient HttpClient = new();

    private readonly AppPaths _paths;
    private readonly ProcessRunner _runner;
    private readonly IrecoveryClient _irecovery;
    private readonly DriverManager _drivers;
    private readonly Action<string>? _log;

    public BootWorkflow(AppPaths paths, ProcessRunner runner, IrecoveryClient irecovery, DriverManager drivers, Action<string>? log = null)
    {
        _paths = paths;
        _runner = runner;
        _irecovery = irecovery;
        _drivers = drivers;
        _log = log;
    }

    public static readonly Dictionary<(string Cpid, string Bdid), DeviceInfo> A12A13Devices = new(StringTupleComparer.Instance)
    {
        [("8020", "0a")] = new("iPhone XS Max", "d331", "iPhone11,6"),
        [("8020", "0c")] = new("iPhone XR", "n841", "iPhone11,8"),
        [("8020", "0e")] = new("iPhone XS", "d321", "iPhone11,2"),
        [("8020", "1a")] = new("iPhone XS Max", "d331p", "iPhone11,6"),
        [("8020", "14")] = new("iPad mini 5", "j210", "iPad11,1"),
        [("8020", "16")] = new("iPad mini 5", "j210", "iPad11,2"),
        [("8020", "1c")] = new("iPad Air 3", "j217", "iPad11,3"),
        [("8020", "1e")] = new("iPad Air 3", "j217", "iPad11,4"),
        [("8020", "24")] = new("iPad 8th gen", "ipad11b", "iPad11,6"),
        [("8020", "26")] = new("iPad 8th gen", "ipad11b", "iPad11,7"),
        [("8030", "02")] = new("iPhone 11 Pro Max", "d431", "iPhone12,5"),
        [("8030", "04")] = new("iPhone 11", "n104", "iPhone12,1"),
        [("8030", "06")] = new("iPhone 11 Pro", "d421", "iPhone12,3"),
        [("8030", "10")] = new("iPhone SE 2nd gen", "d79", "iPhone12,8"),
        [("8030", "08")] = new("iPad 9th gen", "j181", "iPad12,1"),
        [("8030", "0a")] = new("iPad 9th gen", "j181", "iPad12,2")
    };

    public static readonly Dictionary<(string Cpid, string Bdid), DeviceInfo> A7A11Devices = new(StringTupleComparer.Instance)
    {
        [("8960", "02")] = new("iPad Air 2", "j81", "iPad5,3"),
        [("8960", "04")] = new("iPad Air 2", "j82", "iPad5,4"),
        [("8000", "04")] = new("iPhone 6s", "n71", "iPhone8,1"),
        [("8000", "06")] = new("iPhone 6s Plus", "n66", "iPhone8,2"),
        [("8000", "08")] = new("iPhone SE (1st gen)", "n69", "iPhone8,4"),
        [("8001", "08")] = new("iPad Pro (12.9\")", "j98a", "iPad6,8"),
        [("8001", "0a")] = new("iPad Pro (12.9\")", "j99a", "iPad6,8"),
        [("8010", "10")] = new("iPad (6th gen)", "j71", "iPad7,11"),
        [("8010", "12")] = new("iPad (6th gen)", "j72", "iPad7,12"),
        [("8011", "08")] = new("iPad Pro (10.5\")", "j207", "iPad7,3"),
        [("8011", "0a")] = new("iPad Pro (10.5\")", "j208", "iPad7,4"),
        [("8015", "06")] = new("iPhone 8", "d20", "iPhone10,1"),
        [("8015", "08")] = new("iPhone 8 Plus", "d22", "iPhone10,2"),
        [("8015", "0a")] = new("iPhone X", "d221", "iPhone10,3"),
        [("8015", "0c")] = new("iPhone 8", "d201", "iPhone10,4"),
        [("8015", "0e")] = new("iPhone 8 Plus", "d223", "iPhone10,5"),
        [("8015", "10")] = new("iPhone X", "d220", "iPhone10,6")
    };

    public async Task BootDiagA7A11Async(string productTypeOrArchiveName, CancellationToken cancellationToken = default)
    {
        Log("=== DIAG Boot Sequence (A7-A11 / gaster) ===");
        CleanSwap();
        var archiveName = productTypeOrArchiveName;
        var zipPath = Path.Combine(_paths.SwapDirectory, archiveName + ".zip");
        await DownloadAsync($"https://kratius.site/purple/{archiveName}.zip", zipPath, cancellationToken).ConfigureAwait(false);
        ExtractZip(zipPath);

        var iboot = FindRequiredFile("iBoot.img4", "iBoot*");
        var diag = FindRequiredFile("diag.im4", "diag*");

        Log("Sending iBoot.img4 via irecovery.exe...");
        await UploadIrecoveryAsync(iboot, cancellationToken).ConfigureAwait(false);
        Log("Waiting for device to enter Recovery mode...");
        if (!await _irecovery.WaitForRecoveryAsync(30, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Device did not enter Recovery mode after iBoot boot");
        }

        Log("Setting boot-args usbserial=enable...");
        await IrecoveryCommandAsync("setenvnp boot-args usbserial=enable", cancellationToken).ConfigureAwait(false);
        await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
        Log("Saving environment...");
        await IrecoveryCommandAsync("saveenv", cancellationToken).ConfigureAwait(false);
        await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
        Log($"Sending diag image: {Path.GetFileName(diag)}...");
        await UploadIrecoveryAsync(diag, cancellationToken).ConfigureAwait(false);
        Log("Booting diag image...");
        await IrecoveryCommandAsync("go", cancellationToken).ConfigureAwait(false);
        await Task.Delay(3000, cancellationToken).ConfigureAwait(false);
        Log("Device booted to DIAG mode. Check Device Manager for the DIAG CDC serial port.");
    }

    public async Task BootDiagA12A13Async(string archiveName, CancellationToken cancellationToken = default)
    {
        Log("=== DIAG Boot Sequence (A12-A13) ===");
        CleanSwap();
        var zipPath = Path.Combine(_paths.SwapDirectory, archiveName + ".zip");
        await DownloadAsync($"https://kratius.site/purple/{archiveName}.zip", zipPath, cancellationToken).ConfigureAwait(false);
        ExtractZip(zipPath);

        var ibss = FindRequiredFile("ibss.raw", "*.raw");
        var diag = FindRequiredFile("diag.img4", "diag*");
        Log($"Found ibss.raw={ibss}, diag.img4={diag}");

        Log("Sending iBSS.raw via usbliter8_boot...");
        await SendUsbLiter8BootAsync(ibss, cancellationToken).ConfigureAwait(false);
        Log("Ensuring Apple driver for recovery mode...");
        await _drivers.EnsureAppleDriverAsync(cancellationToken).ConfigureAwait(false);
        Log("Waiting for device to enter Recovery mode...");
        if (!await _irecovery.WaitForRecoveryAsync(15, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Device did not enter Recovery mode after iBSS boot");
        }

        await Task.Delay(2000, cancellationToken).ConfigureAwait(false);
        await IrecoveryCommandAsync("setenvnp boot-args usbserial=enable", cancellationToken).ConfigureAwait(false);
        await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
        await IrecoveryCommandAsync("saveenv", cancellationToken).ConfigureAwait(false);
        await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
        Log($"Sending diag image: {Path.GetFileName(diag)}...");
        await UploadIrecoveryAsync(diag, cancellationToken).ConfigureAwait(false);
        Log("Booting diag image...");
        await IrecoveryCommandAsync("go", cancellationToken).ConfigureAwait(false);
        await Task.Delay(3000, cancellationToken).ConfigureAwait(false);
        Log("Device booted to DIAG mode. Check Device Manager for the DIAG CDC serial port.");
    }

    public async Task BootDetectedDeviceAsync(string cpid, string bdid, CancellationToken cancellationToken = default)
    {
        cpid = NormalizeHex(cpid, 4);
        bdid = NormalizeHex(bdid, 2);

        if (A12A13Devices.TryGetValue((cpid, bdid), out var a12Device))
        {
            Log($"Device identified: {a12Device.Name} ({a12Device.ProductType}) codename={a12Device.Codename} [A12-A13]");
            await BootDiagA12A13Async(a12Device.ProductType, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (A7A11Devices.TryGetValue((cpid, bdid), out var a7Device))
        {
            Log($"Device identified: {a7Device.Name} ({a7Device.ProductType}) codename={a7Device.Codename} [A7-A11]");
            await BootDiagA7A11Async(a7Device.ProductType, cancellationToken).ConfigureAwait(false);
            return;
        }

        throw new NotSupportedException($"Unsupported device. CPID: {cpid}, BDID: {bdid}");
    }

    public async Task EraseDfuAsync(string cpid, string bdid, CancellationToken cancellationToken = default)
    {
        Log("=== Erase DFU Sequence ===");
        cpid = NormalizeHex(cpid, 4);
        bdid = NormalizeHex(bdid, 2);

        var isA7A11 = false;
        if (!A12A13Devices.TryGetValue((cpid, bdid), out var device))
        {
            if (!A7A11Devices.TryGetValue((cpid, bdid), out device))
            {
                throw new NotSupportedException($"Unsupported device. CPID: {cpid}, BDID: {bdid}");
            }
            isA7A11 = true;
        }

        Log($"Device identified: {device.Name} ({device.ProductType}) codename={device.Codename}{(isA7A11 ? " [A7-A11]" : "")}");
        CleanSwap();
        var url = $"https://kratius.site/bypasstool/DFU/{device.ProductType}/iBEC.{device.ProductType}.RELEASE.patched";
        var ibecPath = Path.Combine(_paths.SwapDirectory, $"iBEC.{device.ProductType}.RELEASE.patched");
        await DownloadAsync(url, ibecPath, cancellationToken).ConfigureAwait(false);
        Log("Uploading iBEC via USB control transfer...");
        await SendUsbLiter8BootAsync(ibecPath, cancellationToken).ConfigureAwait(false);
        Log("Ensuring Apple driver for recovery mode...");
        await _drivers.EnsureAppleDriverAsync(cancellationToken).ConfigureAwait(false);
        Log("Waiting for recovery mode (15 sec)...");
        if (!await _irecovery.WaitForRecoveryAsync(15, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Device did not enter Recovery mode after iBEC boot");
        }

        Log("Sending obliteration commands via irecovery...");
        await IrecoveryCommandAsync("setenv oblit-inprogress 5", cancellationToken).ConfigureAwait(false);
        await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
        await IrecoveryCommandAsync("setenv auto-boot true", cancellationToken).ConfigureAwait(false);
        await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        await IrecoveryCommandAsync("saveenv", cancellationToken).ConfigureAwait(false);
        await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
        Log("Rebooting device...");
        await IrecoveryCommandAsync("reboot", cancellationToken).ConfigureAwait(false);
        await Task.Delay(3000, cancellationToken).ConfigureAwait(false);
        Log($"Erase complete. {device.Name} is rebooting and will erase.");
    }

    public async Task SendUsbLiter8BootAsync(string payloadPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_paths.UsbLiter8BootPath))
        {
            throw new FileNotFoundException("USB boot helper not found", _paths.UsbLiter8BootPath);
        }

        Directory.CreateDirectory(_paths.UsbLiter8Directory);
        var staged = Path.Combine(_paths.UsbLiter8Directory, "iboot.raw");
        File.Copy(payloadPath, staged, overwrite: true);
        Log($"Copied payload to {staged}");

        var helperName = Path.GetFileNameWithoutExtension(_paths.UsbLiter8BootPath);
        var helperArgs = string.Equals(helperName, "usbliter8_boot", StringComparison.OrdinalIgnoreCase)
            ? new[] { "iboot.raw" }
            : ["boot", "iboot.raw"];

        var result = await _runner.RunAsync(
            _paths.UsbLiter8BootPath,
            helperArgs,
            _paths.UsbLiter8Directory,
            TimeSpan.FromSeconds(60),
            cancellationToken,
            line => Log("[usbliter8] " + line)).ConfigureAwait(false);

        Log($"usbliter8_boot exit code: {result.ExitCode}");
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"usbliter8_boot failed (code {result.ExitCode}): {result.CombinedOutput}");
        }

        try { File.Delete(staged); } catch { }
        await Task.Delay(3000, cancellationToken).ConfigureAwait(false);
    }

    private async Task UploadIrecoveryAsync(string path, CancellationToken cancellationToken)
    {
        var result = await _irecovery.UploadFileAsync(path, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            Log($"[irecovery] WARNING: exit code {result.ExitCode}");
        }
    }

    private async Task IrecoveryCommandAsync(string command, CancellationToken cancellationToken)
    {
        var result = await _irecovery.CommandAsync(command, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            Log($"[irecovery] WARNING: exit code {result.ExitCode}");
        }
    }

    private async Task DownloadAsync(string url, string destination, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        Log($"Downloading {url}");
        using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 8192, useAsync: true);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        Log("Download complete");
    }

    private void ExtractZip(string zipPath)
    {
        Log("Extracting boot chain...");
        ZipFile.ExtractToDirectory(zipPath, _paths.SwapDirectory, overwriteFiles: true);
    }

    private void CleanSwap()
    {
        if (Directory.Exists(_paths.SwapDirectory))
        {
            Directory.Delete(_paths.SwapDirectory, recursive: true);
        }

        Directory.CreateDirectory(_paths.SwapDirectory);
    }

    private string FindRequiredFile(string preferredName, string pattern)
    {
        var preferred = Directory.GetFiles(_paths.SwapDirectory, preferredName, SearchOption.AllDirectories).FirstOrDefault();
        var matched = preferred ?? Directory.GetFiles(_paths.SwapDirectory, pattern, SearchOption.AllDirectories).FirstOrDefault();
        if (matched == null || !File.Exists(matched))
        {
            throw new FileNotFoundException($"{preferredName} not found in boot chain");
        }

        return matched;
    }

    private static string NormalizeHex(string value, int minWidth = 0)
    {
        var normalized = value.Replace("0x", "", StringComparison.OrdinalIgnoreCase).Trim().ToLowerInvariant();
        return minWidth <= 0 ? normalized : normalized.PadLeft(minWidth, '0');
    }

    private void Log(string message) => _log?.Invoke(message);
}

public sealed record DeviceInfo(string Name, string Codename, string ProductType);

internal sealed class StringTupleComparer : IEqualityComparer<(string Cpid, string Bdid)>
{
    public static readonly StringTupleComparer Instance = new();

    public bool Equals((string Cpid, string Bdid) x, (string Cpid, string Bdid) y)
    {
        return string.Equals(x.Cpid, y.Cpid, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(x.Bdid, y.Bdid, StringComparison.OrdinalIgnoreCase);
    }

    public int GetHashCode((string Cpid, string Bdid) obj)
    {
        return HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Cpid),
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Bdid));
    }
}
