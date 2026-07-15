namespace CfgUtility.Services;

public sealed class IrecoveryClient
{
    private readonly AppPaths _paths;
    private readonly ProcessRunner _runner;
    private readonly Action<string>? _log;

    public IrecoveryClient(AppPaths paths, ProcessRunner runner, Action<string>? log = null)
    {
        _paths = paths;
        _runner = runner;
        _log = log;
    }

    public Task<CommandResult> RunAsync(
        IEnumerable<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return _runner.RunAsync(
            _paths.IrecoveryPath,
            arguments,
            _paths.FilesDirectory,
            timeout ?? TimeSpan.FromSeconds(30),
            cancellationToken,
            line => _log?.Invoke("[irecovery] " + line));
    }

    public async Task<string> QueryRawAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["-q"], TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
        return result.CombinedOutput;
    }

    public async Task<Dictionary<string, string>> QueryAsync(CancellationToken cancellationToken = default)
    {
        return ParseQuery(await QueryRawAsync(cancellationToken).ConfigureAwait(false));
    }

    public Task<CommandResult> CommandAsync(string command, CancellationToken cancellationToken = default)
    {
        return RunAsync(["-c", command], TimeSpan.FromSeconds(30), cancellationToken);
    }

    public Task<CommandResult> UploadFileAsync(string path, CancellationToken cancellationToken = default)
    {
        return RunAsync(["-f", path], TimeSpan.FromSeconds(30), cancellationToken);
    }

    public async Task<bool> WaitForRecoveryAsync(int seconds, CancellationToken cancellationToken = default)
    {
        var attempts = Math.Max(1, seconds * 2);
        for (var i = 0; i < attempts; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await RunAsync(["-q"], TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
                if (result.ExitCode == 0)
                {
                    return true;
                }
            }
            catch
            {
            }

            if (i % 10 == 0)
            {
                _log?.Invoke($"Waiting... {i / 2 + 1}s");
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    public static Dictionary<string, string> ParseQuery(string output)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(':', 2);
            if (parts.Length == 2)
            {
                values[parts[0].Trim().ToUpperInvariant()] = parts[1].Trim();
            }
        }

        return values;
    }
}
