using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace AuthService.Services
{
    public sealed class SmtpOptions
    {
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 587;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string From { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
    }

    public class SmtpEmailSender : IEmailSender
    {
        private readonly SmtpOptions _opts;
        private readonly ILogger<SmtpEmailSender> _logger;

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
                From = section["From"] ?? section["Username"] ?? string.Empty,
                DisplayName = section["DisplayName"]
            };
        }

        public async Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
        {
            var fromAddress = MailboxAddress.Parse(_opts.From);
            var displayName = string.IsNullOrWhiteSpace(_opts.DisplayName)
                ? (string.IsNullOrWhiteSpace(fromAddress.Name) ? "PantMig" : fromAddress.Name)
                : _opts.DisplayName!;
            var from = new MailboxAddress(displayName, fromAddress.Address);

            var message = new MimeMessage();
            message.From.Add(from);
            message.Sender = from;
            message.ReplyTo.Add(from);
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;

            try
            {
                var domain = from.Address.Split('@').LastOrDefault() ?? "pantmig.dk";
                message.MessageId = $"{Guid.NewGuid():N}@{domain}";
            }
            catch { }

            var plain = new TextPart("plain") { Text = body };
            var htmlBody = System.Net.WebUtility.HtmlEncode(body).Replace("\n", "<br>");
            var html = new TextPart("html") { Text = $"<html><body><p>{htmlBody}</p></body></html>" };
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
                try { await client.DisconnectAsync(true, ct); } catch { }
            }
        }
    }
}
