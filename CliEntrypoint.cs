using CfgUtility.Services;

namespace CfgUtility;

public static class CliEntrypoint
{
public static async Task<int> RunAsync(string[] args)
{
var log = new Action<string>(Console.WriteLine);
var paths = new AppPaths();
var runner = new ProcessRunner();
var irecovery = new IrecoveryClient(paths, runner, log);
var quietIrecovery = new IrecoveryClient(paths, runner);
var drivers = new DriverManager(paths, runner, log);
var diag = new DiagSerialService(runner);
var boot = new BootWorkflow(paths, runner, irecovery, drivers, log);
var requestedExitCode = 0;

try
{
    if (args.Length == 0 || IsHelp(args[0]))
    {
        PrintUsage(paths);
        return 0;
    }

    switch (args[0].ToLowerInvariant())
    {
        case "query":
            await QueryAsync(args.Skip(1).ToArray());
            break;
        case "irecovery":
            await RunIrecoveryAsync(args.Skip(1).ToArray());
            break;
        case "wait-recovery":
            await WaitRecoveryAsync(args.Skip(1).ToArray());
            break;
        case "diag-port":
            await PrintDiagPortAsync();
            break;
        case "diag-at":
            await ReadAtIdentityAsync(args.Skip(1).ToArray());
            break;
        case "syscfg-read":
            await ReadSyscfgAsync(args.Skip(1).ToArray());
            break;
        case "syscfg-flash":
            await FlashSyscfgAsync(args.Skip(1).ToArray());
            break;
        case "pair-bt":
            RequireArgCount(args, 2, "pair-bt <wifi-mac>");
            Console.WriteLine(SyscfgCodec.PairBluetoothMac(args[1]));
            break;
        case "syscfg-mac":
            RequireArgCount(args, 2, "syscfg-mac <mac>");
            Console.WriteLine(SyscfgCodec.ToSyscfgMacValue(args[1]));
            break;
        case "fix-driver":
            await drivers.FixAppleUsbDriverAsync();
            break;
        case "install-libusbk":
            await drivers.InstallLibUsbKForDfuAsync();
            break;
        case "purple-a7":
            RequireArgCount(args, 2, "purple-a7 <archive-or-product>");
            await boot.BootDiagA7A11Async(args[1]);
            break;
        case "purple-a12":
            RequireArgCount(args, 2, "purple-a12 <archive-or-product>");
            await boot.BootDiagA12A13Async(args[1]);
            break;
        case "purple-auto":
            await BootDetectedDeviceAsync(args.Skip(1).ToArray());
            break;
        case "purple-erase":
            await RunDiagEraseAsync(args.Skip(1).ToArray());
            break;
        case "erase":
            RequireArgCount(args, 3, "erase <cpid> <bdid>");
            await boot.EraseDfuAsync(args[1], args[2]);
            break;
        case "erase-auto":
            await RunDfuEraseFromConnectedDeviceAsync();
            break;
        case "paths":
            PrintPaths(paths);
            break;
        default:
            throw new ArgumentException($"Unknown command: {args[0]}");
    }

    return requestedExitCode;
}
catch (Exception ex)
{
    Console.Error.WriteLine("ERROR: " + ex.Message);
    return 1;
}

async Task QueryAsync(string[] commandArgs)
{
    var raw = commandArgs.Contains("--raw", StringComparer.OrdinalIgnoreCase);
    if (raw)
    {
        Console.Write(await quietIrecovery.QueryRawAsync());
        return;
    }

    var values = await quietIrecovery.QueryAsync();
    if (values.Count == 0)
    {
        Console.WriteLine("No irecovery values returned.");
        return;
    }

    foreach (var pair in values.OrderBy(p => p.Key))
    {
        Console.WriteLine($"{pair.Key}: {pair.Value}");
    }
}

async Task RunIrecoveryAsync(string[] commandArgs)
{
    var split = Array.IndexOf(commandArgs, "--");
    var forwarded = split >= 0 ? commandArgs.Skip(split + 1).ToArray() : commandArgs;
    if (forwarded.Length == 0)
    {
        throw new ArgumentException("irecovery requires arguments, for example: irecovery -- -q");
    }

    var result = await quietIrecovery.RunAsync(forwarded);
    Console.Write(result.Output);
    Console.Error.Write(result.Error);
    requestedExitCode = result.ExitCode;
}

async Task WaitRecoveryAsync(string[] commandArgs)
{
    var seconds = commandArgs.Length > 0 && int.TryParse(commandArgs[0], out var parsed) ? parsed : 30;
    Console.WriteLine(await quietIrecovery.WaitForRecoveryAsync(seconds) ? "Recovery detected." : "Recovery not detected.");
}

async Task PrintDiagPortAsync()
{
    var port = await diag.FindDiagPortAsync();
    Console.WriteLine(string.IsNullOrWhiteSpace(port) ? "No DIAG serial port found." : port);
}

async Task ReadAtIdentityAsync(string[] commandArgs)
{
    var port = await ResolvePortAsync(commandArgs);
    Console.WriteLine(await diag.ReadAtIdentityAsync(port));
}

async Task ReadSyscfgAsync(string[] commandArgs)
{
    var port = await ResolvePortAsync(commandArgs);
    var values = await diag.ReadSyscfgAsync(port);
    Console.WriteLine($"Serial: {values.Serial}");
    Console.WriteLine($"Wi-Fi:  {values.WifiMac}");
    Console.WriteLine($"BT:     {values.BluetoothMac}");
}

async Task FlashSyscfgAsync(string[] commandArgs)
{
    var serial = ValueAfter(commandArgs, "--serial") ?? ValueAfter(commandArgs, "-s");
    if (string.IsNullOrWhiteSpace(serial))
    {
        throw new ArgumentException("syscfg-flash requires --serial <serial>");
    }

    var port = await ResolvePortAsync(commandArgs);
    var wifi = ValueAfter(commandArgs, "--wifi") ?? ValueAfter(commandArgs, "-w");
    var bluetooth = ValueAfter(commandArgs, "--bt") ?? ValueAfter(commandArgs, "-b");
    if (string.IsNullOrWhiteSpace(bluetooth) && !string.IsNullOrWhiteSpace(wifi))
    {
        bluetooth = SyscfgCodec.PairBluetoothMac(wifi);
    }

    await diag.FlashSyscfgAsync(port, serial, wifi, bluetooth);
    Console.WriteLine("syscfg write commands sent.");
}

async Task RunDiagEraseAsync(string[] commandArgs)
{
    var port = await ResolvePortAsync(commandArgs);
    await diag.RunDiagEraseAsync(port);
    Console.WriteLine("DIAG erase commands sent.");
}

async Task BootDetectedDeviceAsync(string[] commandArgs)
{
    string? cpid = null;
    string? bdid = null;

    if (commandArgs.Length >= 2)
    {
        cpid = commandArgs[0];
        bdid = commandArgs[1];
    }
    else
    {
        var values = await quietIrecovery.QueryAsync();
        values.TryGetValue("CPID", out cpid);
        values.TryGetValue("BDID", out bdid);
    }

    if (string.IsNullOrWhiteSpace(cpid) || string.IsNullOrWhiteSpace(bdid))
    {
        throw new InvalidOperationException("Could not read CPID/BDID. Pass them explicitly or connect a device in DFU/recovery mode.");
    }

    await boot.BootDetectedDeviceAsync(cpid, bdid);
}

async Task RunDfuEraseFromConnectedDeviceAsync()
{
    var values = await quietIrecovery.QueryAsync();
    if (!values.TryGetValue("CPID", out var cpid) || string.IsNullOrWhiteSpace(cpid))
    {
        throw new InvalidOperationException("Could not read CPID. Make sure the device is in PWND DFU mode.");
    }

    if (!values.TryGetValue("BDID", out var bdid) || string.IsNullOrWhiteSpace(bdid))
    {
        throw new InvalidOperationException("Could not read BDID. Make sure the device is in PWND DFU mode.");
    }

    await boot.EraseDfuAsync(cpid, bdid);
}

async Task<string> ResolvePortAsync(string[] commandArgs)
{
    var port = ValueAfter(commandArgs, "--port") ?? ValueAfter(commandArgs, "-p");
    if (!string.IsNullOrWhiteSpace(port))
    {
        return port;
    }

    port = await diag.FindDiagPortAsync();
    if (string.IsNullOrWhiteSpace(port))
    {
        throw new InvalidOperationException("No DIAG serial port found. Pass --port explicitly.");
    }

    return port;
}

static string? ValueAfter(string[] values, string name)
{
    for (var i = 0; i < values.Length - 1; i++)
    {
        if (string.Equals(values[i], name, StringComparison.OrdinalIgnoreCase))
        {
            return values[i + 1];
        }
    }

    return null;
}

static bool IsHelp(string value)
{
    return value is "-h" or "--help" or "help";
}

static void RequireArgCount(string[] values, int count, string usage)
{
    if (values.Length < count)
    {
        throw new ArgumentException("Usage: " + usage);
    }
}

static void PrintUsage(AppPaths paths)
{
    Console.WriteLine("CFG utility CLI");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  paths");
    Console.WriteLine("  query [--raw]");
    Console.WriteLine("  irecovery -- <args>");
    Console.WriteLine("  wait-recovery [seconds]");
    Console.WriteLine("  diag-port");
    Console.WriteLine("  diag-at [--port PORT]");
    Console.WriteLine("  syscfg-read [--port PORT]");
    Console.WriteLine("  syscfg-flash [--port PORT] --serial SN [--wifi MAC] [--bt MAC]");
    Console.WriteLine("  pair-bt <wifi-mac>");
    Console.WriteLine("  syscfg-mac <mac>");
    Console.WriteLine("  fix-driver");
    Console.WriteLine("  install-libusbk");
    Console.WriteLine("  purple-a7 <archive-or-product>");
    Console.WriteLine("  purple-a12 <archive-or-product>");
    Console.WriteLine("  purple-auto [cpid bdid]");
    Console.WriteLine("  purple-erase [--port PORT]");
    Console.WriteLine("  erase <cpid> <bdid>");
    Console.WriteLine("  erase-auto");
}

static void PrintPaths(AppPaths paths)
{
    Console.WriteLine($"Base:          {paths.BaseDirectory}");
    Console.WriteLine($"Files:         {paths.FilesDirectory}");
    Console.WriteLine($"Swap:          {paths.SwapDirectory}");
    Console.WriteLine($"irecovery:     {paths.IrecoveryPath}");
    Console.WriteLine($"usbliter8:     {paths.UsbLiter8BootPath}");
    Console.WriteLine($"drivers:       {paths.DriversDirectory}");
}
}
}
