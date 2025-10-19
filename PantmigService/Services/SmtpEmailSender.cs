using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace PantmigService.Services
{
    public sealed class SmtpOptions
    {
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 587;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string From { get; set; } = string.Empty;
    }

    public class SmtpEmailSender : IEmailSender
    {
        private readonly SmtpOptions _opts;
        private readonly ILogger<SmtpEmailSender> _logger;
        private readonly string _apiDomain;

        public SmtpEmailSender(IConfiguration config, ILogger<SmtpEmailSender> logger)
        {
            _logger = logger;
            var section = config.GetSection("Smtp");
            _opts = new SmtpOptions
            {
                Host = section["Host"] ?? string.Empty,
                Port = int.TryParse(section["Port"], out var p) ? p : 587,
                Username = section["Username"] ?? string.Empty,
                Password = section["Password"] ?? string.Empty,
                From = section["From"] ?? section["Username"] ?? string.Empty
            };
            _apiDomain = config["Domain"] ?? config["Urls"] ?? "pantmig.dk";
        }

        public async Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
        {
            var fromAddress = MailboxAddress.Parse(_opts.From);
            var displayName = string.IsNullOrWhiteSpace(fromAddress.Name) ? "PantMig" : fromAddress.Name;
            var from = new MailboxAddress(displayName, fromAddress.Address);

            var message = new MimeMessage();
            message.From.Add(from);
            message.Sender = from;
            message.ReplyTo.Add(from);
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;

            // Set a stable Message-Id using our domain to avoid machine-name IDs
            try
            {
                var domain = from.Address.Split('@').LastOrDefault() ?? "pantmig.dk";
                message.MessageId = $"{Guid.NewGuid():N}@{domain}";
            }
            catch { /* ignore */ }

            // List-Unsubscribe headers (mailto and one-click URL)
            try
            {
                var unsubscribeUrl = $"https://{_apiDomain.TrimEnd('/')}/newsletter/unsubscribe?email={Uri.EscapeDataString(to)}";
                message.Headers.Add("List-Unsubscribe", $"<mailto:{from.Address}?subject=unsubscribe>, <{unsubscribeUrl}>");
                message.Headers.Add("List-Unsubscribe-Post", "List-Unsubscribe=One-Click");
            }
            catch { /* ignore */ }

            // Multipart/alternative: plain + HTML
            var plain = new TextPart("plain") { Text = body };
            var htmlBody = System.Net.WebUtility.HtmlEncode(body).Replace("\n", "<br>");
            var html = new TextPart("html") { Text = $"<html><body><p>{htmlBody}</p><hr><p style=\"font-size:12px;color:#666\">If you no longer wish to receive emails, you can <a href=\"https://{_apiDomain}/newsletter/unsubscribe?email={System.Uri.EscapeDataString(to)}\">unsubscribe here</a>.</p></body></html>" };
            message.Body = new MultipartAlternative { plain, html };

            using var client = new SmtpClient();
            try
            {
                await client.ConnectAsync(_opts.Host, _opts.Port, SecureSocketOptions.StartTls, ct);
                if (!string.IsNullOrEmpty(_opts.Username))
                {
                    await client.AuthenticateAsync(_opts.Username, _opts.Password, ct);
                }
                await client.SendAsync(message, ct);
            }
            finally
            {
                try { await client.DisconnectAsync(true, ct); } catch { /* ignore */ }
            }
        }
    }
}
