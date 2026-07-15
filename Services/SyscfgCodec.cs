using System.Globalization;

namespace CfgUtility.Services;

public static class SyscfgCodec
{
    public static string ParseSerial(string response)
    {
        if (string.IsNullOrWhiteSpace(response) ||
            response.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        foreach (var rawLine in response.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim().TrimStart('>').Trim();
            if (line.Length == 0 || line.StartsWith("syscfg", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (line.Contains("Serial:", StringComparison.OrdinalIgnoreCase))
            {
                return line.Replace("Serial:", "", StringComparison.OrdinalIgnoreCase).Trim();
            }

            return line;
        }

        return "";
    }

    public static string ParseMac(string response)
    {
        if (string.IsNullOrWhiteSpace(response) ||
            response.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        var bytes = new List<string>();
        foreach (var token in response.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var value = token.Replace("0x", "", StringComparison.OrdinalIgnoreCase).Trim();
            while (value.Length >= 2)
            {
                bytes.Add(value[^2..].ToUpperInvariant());
                value = value[..^2];
            }
        }

        return bytes.Count < 6 ? "" : string.Join(":", bytes.Take(6));
    }

    public static string PairBluetoothMac(string wifiMac)
    {
        if (string.IsNullOrWhiteSpace(wifiMac))
        {
            return "";
        }

        var parts = wifiMac.Split(':');
        if (parts.Length != 6)
        {
            return "";
        }

        if (!byte.TryParse(parts[5], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var lastByte))
        {
            return wifiMac;
        }

        parts[5] = ((byte)(lastByte ^ 1)).ToString("X2", CultureInfo.InvariantCulture);
        return string.Join(":", parts.Select(p => p.ToUpperInvariant()));
    }

    public static string ToSyscfgMacValue(string mac)
    {
        var parts = mac.Split(':');
        if (parts.Length != 6)
        {
            return mac;
        }

        return $"0x{parts[3]}{parts[2]}{parts[1]}{parts[0]} 0x0000{parts[5]}{parts[4]} 0x00000000 0x00000000";
    }
}
