using SendGrid;

namespace ExcluSightsLibrary.DiscordServices
{
    public sealed class SendGridEmailSender : IEmailSender
    {
        private readonly SendGridClient _client;
        private readonly string _fromEmail;
        private readonly string _fromName;

        public SendGridEmailSender(string apiKey, string fromEmail, string fromName)
        {
            _client = new SendGridClient(apiKey);
            _fromEmail = fromEmail;
        }
        public async Task SendAsync(string to, string subject, string htmlBody, (string FileName, string ContentType, byte[] Bytes)? attachment = null, CancellationToken ct = default)
        {
            var msg = new SendGrid.Helpers.Mail.SendGridMessage
            {
                From = new SendGrid.Helpers.Mail.EmailAddress(_fromEmail, _fromName),
                Subject = subject,
                HtmlContent = htmlBody
            };
            msg.AddTo(new SendGrid.Helpers.Mail.EmailAddress(to));
            if (attachment.HasValue)
            {
                var a = attachment.Value;
                msg.AddAttachment(a.FileName, Convert.ToBase64String(a.Bytes), a.ContentType);
            }
            var resp = await _client.SendEmailAsync(msg, ct);
            if ((int)resp.StatusCode >= 400)
            {
                var body = await resp.Body.ReadAsStringAsync(ct);
                throw new InvalidOperationException($"Email failed: {(int)resp.StatusCode} {body}");
            }
        }
    }
}
