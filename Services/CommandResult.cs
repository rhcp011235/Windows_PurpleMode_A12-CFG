namespace CfgUtility.Services;

public sealed record CommandResult(int ExitCode, string Output, string Error)
{
    public string CombinedOutput => string.IsNullOrWhiteSpace(Error)
        ? Output
        : string.Concat(Output, string.IsNullOrWhiteSpace(Output) ? "" : Environment.NewLine, Error);
}
