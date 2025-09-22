namespace PantmigService.Security;

public sealed class NoOpAntivirusScanner : IAntivirusScanner
{
    public Task<AntivirusScanResult> ScanAsync(Stream content, string? fileName = null, CancellationToken ct = default)
        => Task.FromResult(AntivirusScanResult.Clean());
}
