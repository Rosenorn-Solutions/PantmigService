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
        }

        public async Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
        {
            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(_opts.From));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;
            message.Body = new TextPart("plain") { Text = body };

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
