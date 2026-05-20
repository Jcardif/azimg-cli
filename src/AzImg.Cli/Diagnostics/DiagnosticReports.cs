namespace AzImg.Cli.Diagnostics;

/// <summary>
/// One diagnostic check produced by the doctor command.
/// </summary>
/// <param name="Name">The stable check name.</param>
/// <param name="Passed">Whether the check passed.</param>
/// <param name="Message">A human-readable explanation of the check result.</param>
public sealed record DiagnosticCheck(string Name, bool Passed, string Message);

/// <summary>
/// Full diagnostic report produced by the doctor command.
/// </summary>
/// <param name="ConfigPath">The resolved configuration path used for the command.</param>
/// <param name="ProfileName">The resolved profile name.</param>
/// <param name="Checks">The diagnostic checks that were run.</param>
public sealed record DiagnosticReport(
    string ConfigPath,
    string? ProfileName,
    IReadOnlyList<DiagnosticCheck> Checks)
{
    /// <summary>Gets a value indicating whether every diagnostic check passed.</summary>
    public bool IsHealthy => Checks.All(static check => check.Passed);
}

/// <summary>
/// JSON shape emitted by <c>azimg doctor</c> unless <c>--format text</c> is passed.
/// </summary>
/// <param name="ConfigPath">The resolved configuration path used for the command.</param>
/// <param name="ProfileName">The resolved profile name.</param>
/// <param name="Checks">The diagnostic checks that were run.</param>
/// <param name="IsHealthy">Whether every diagnostic check passed.</param>
public sealed record DiagnosticReportDocument(
    string ConfigPath,
    string? ProfileName,
    DiagnosticCheck[] Checks,
    bool IsHealthy);