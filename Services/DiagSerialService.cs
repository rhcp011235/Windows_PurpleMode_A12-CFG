using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;

namespace CfgUtility.Services;

public sealed record SyscfgValues(string Serial, string WifiMac, string BluetoothMac);

public sealed class DiagSerialService
{
    private readonly ProcessRunner _runner;

    public DiagSerialService(ProcessRunner runner)
    {
        _runner = runner;
    }

    public async Task<string?> FindDiagPortAsync(CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsWindows())
        {
            const string script =
                "Get-CimInstance Win32_PnPEntity | " +
                "Where-Object { ($_.Name -match 'DIAG|CDC|Apple Mobile Device') -and ($_.DeviceID -match 'COM\\d+') } | " +
                "Select-Object -First 1 -ExpandProperty DeviceID";
            var result = await _runner.RunAsync(
                "powershell",
                ["-NoProfile", "-Command", script],
                timeout: TimeSpan.FromSeconds(10),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var match = Regex.Match(result.CombinedOutput, "COM\\d+", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Value.ToUpperInvariant();
            }
        }

        return SerialPort.GetPortNames()
            .FirstOrDefault(p =>
                p.Contains("diag", StringComparison.OrdinalIgnoreCase) ||
                p.Contains("usbmodem", StringComparison.OrdinalIgnoreCase) ||
                p.Contains("usbserial", StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith("COM", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<string> ReadAtIdentityAsync(string portName, CancellationToken cancellationToken = default)
    {
        using var port = Open(portName, 115200, 3000);
        port.Open();
        port.Write("AT+GSN\r\n");
        await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        var imei = port.ReadExisting().Trim();
        port.Write("AT+GBSN\r\n");
        await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        var serial = port.ReadExisting().Trim();
        return $"IMEI:{imei}|SN:{serial}";
    }

    public async Task<SyscfgValues> ReadSyscfgAsync(string portName, CancellationToken cancellationToken = default)
    {
        using var port = Open(portName, 9600, 5000);
        port.Open();
        await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        port.DiscardInBuffer();

        var serial = SyscfgCodec.ParseSerial(await SendCommandAsync(port, "syscfg print SrNm", cancellationToken).ConfigureAwait(false));
        var wifi = SyscfgCodec.ParseMac(await SendCommandAsync(port, "syscfg print WMac", cancellationToken).ConfigureAwait(false));
        var bluetooth = SyscfgCodec.ParseMac(await SendCommandAsync(port, "syscfg print BMac", cancellationToken).ConfigureAwait(false));
        return new SyscfgValues(serial, wifi, bluetooth);
    }

    public async Task FlashSyscfgAsync(
        string portName,
        string serial,
        string? wifiMac,
        string? bluetoothMac,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            throw new ArgumentException("Serial number is required.", nameof(serial));
        }

        using var port = Open(portName, 9600, 5000);
        port.Open();
        await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        port.DiscardInBuffer();

        port.Write($"syscfg add SrNm {serial}");
        await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
        _ = port.ReadExisting();

        if (!string.IsNullOrWhiteSpace(wifiMac))
        {
            port.Write($"syscfg add WMac {SyscfgCodec.ToSyscfgMacValue(wifiMac)}");
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(bluetoothMac))
        {
            port.Write($"syscfg add BMac {SyscfgCodec.ToSyscfgMacValue(bluetoothMac)}");
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        port.Write("syscfg save");
        await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        port.Write("reset");
    }

    public async Task RunDiagEraseAsync(string portName, CancellationToken cancellationToken = default)
    {
        using var port = Open(portName, 9600, 5000);
        port.Open();
        await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        port.DiscardInBuffer();

        port.Write("nvram --set oblit-inprogress 5");
        await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
        _ = port.ReadExisting();

        port.Write("nvram --save");
        await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
        _ = port.ReadExisting();

        port.Write("reset");
        await Task.Delay(2000, cancellationToken).ConfigureAwait(false);
    }

    private static SerialPort Open(string portName, int baudRate, int timeout)
    {
        return new SerialPort(portName, baudRate)
        {
            DataBits = 8,
            Parity = Parity.None,
            StopBits = StopBits.One,
            ReadTimeout = timeout,
            WriteTimeout = timeout,
            NewLine = "\n"
        };
    }

    private static async Task<string> SendCommandAsync(SerialPort port, string command, CancellationToken cancellationToken)
    {
        port.DiscardInBuffer();
        port.Write(command);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        var buffer = new StringBuilder();

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            if (port.IsOpen && port.BytesToRead > 0)
            {
                buffer.Append(port.ReadExisting());
            }

            var text = buffer.ToString();
            if (text.Contains('>') || text.Contains("not found", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }

        var result = buffer.ToString().Trim();
        if (result.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        return result;
    }
}
