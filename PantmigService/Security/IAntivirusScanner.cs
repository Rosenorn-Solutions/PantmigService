namespace PantmigService.Security;

public enum AntivirusScanStatus
{
    Clean,
    Infected,
    Error
}

public sealed record AntivirusScanResult(AntivirusScanStatus Status, string? Signature = null, string? Error = null)
{
    public static AntivirusScanResult Clean() => new(AntivirusScanStatus.Clean);
    public static AntivirusScanResult Infected(string signature) => new(AntivirusScanStatus.Infected, signature);
    public static AntivirusScanResult FromError(string error) => new(AntivirusScanStatus.Error, Error: error);
}

public interface IAntivirusScanner
{
    Task<AntivirusScanResult> ScanAsync(Stream content, string? fileName = null, CancellationToken ct = default);
}
