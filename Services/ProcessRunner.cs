using System.Diagnostics;
using System.Text;

namespace CfgUtility.Services;

public sealed class ProcessRunner
{
    public async Task<CommandResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default,
        Action<string>? log = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                ? Environment.CurrentDirectory
                : workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        if (!OperatingSystem.IsWindows() && File.Exists(fileName) &&
            string.Equals(Path.GetExtension(fileName), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            var original = fileName;
            var originalArgs = psi.ArgumentList.ToArray();
            psi.FileName = "wine";
            psi.ArgumentList.Clear();
            psi.ArgumentList.Add(original);
            foreach (var argument in originalArgs)
            {
                psi.ArgumentList.Add(argument);
            }
        }

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var output = new StringBuilder();
        var error = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            output.AppendLine(e.Data);
            log?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            error.AppendLine(e.Data);
            log?.Invoke(e.Data);
        };

        try
        {
            if (!process.Start())
            {
                return new CommandResult(-1, "", "Failed to start process.");
            }
        }
        catch (Exception ex)
        {
            return new CommandResult(-1, "", ex.Message);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = timeout.HasValue ? new CancellationTokenSource(timeout.Value) : null;
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts?.Token ?? CancellationToken.None);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
            return new CommandResult(process.ExitCode, output.ToString(), error.ToString());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return new CommandResult(-1, output.ToString(), $"TIMEOUT: {Path.GetFileName(fileName)} did not finish within {timeout}.");
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }
}
