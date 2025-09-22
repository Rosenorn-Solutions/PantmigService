using System.Linq;
using nClam;

namespace PantmigService.Security;

public class ClamAvOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 3310;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    public bool Enabled { get; set; } = true;
}

public sealed class ClamAvAntivirusScanner : IAntivirusScanner
{
    private readonly ClamAvOptions _options;

    public ClamAvAntivirusScanner(ClamAvOptions options)
    {
        _options = options;
    }

    public async Task<AntivirusScanResult> ScanAsync(Stream content, string? fileName = null, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return AntivirusScanResult.Clean();
        }
        try
        {
            var client = new ClamClient(_options.Host, _options.Port);
            if (content.CanSeek)
                content.Position = 0;
            var result = await client.SendAndScanFileAsync(content, ct);
            return result.Result switch
            {
                ClamScanResults.Clean => AntivirusScanResult.Clean(),
                ClamScanResults.VirusDetected => AntivirusScanResult.Infected(string.Join(",", result.InfectedFiles?.Select(f => f.VirusName) ?? Array.Empty<string>())),
                ClamScanResults.Error => AntivirusScanResult.FromError(result.RawResult),
                _ => AntivirusScanResult.FromError(result.RawResult)
            };
        }
        catch (Exception ex)
        {
            return AntivirusScanResult.FromError(ex.Message);
        }
        finally
        {
            if (content.CanSeek)
                content.Position = 0;
        }
    }
}
