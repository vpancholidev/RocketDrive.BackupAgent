using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Http;
using System.Net.Mail;

namespace RocketDrive.BackupAgent.Services
{
    public interface INotifier
    {
        void Notify(string subject, string body, bool isSuccess);
    }

    public class EmailNotifier : INotifier
    {
        private readonly IConfiguration _cfg;
        public EmailNotifier(IConfiguration cfg) { _cfg = cfg; }

        public void Notify(string subject, string body, bool isSuccess)
        {
            if (!_cfg.GetValue<bool>("Smtp:Enabled")) return;

            try
            {
                using var client = new SmtpClient(_cfg["Smtp:Host"], _cfg.GetValue<int>("Smtp:Port"))
                {
                    EnableSsl = _cfg.GetValue<bool>("Smtp:UseSsl")
                };
                var user = _cfg["Smtp:Username"];
                var pass = _cfg["Smtp:Password"];
                if (!string.IsNullOrWhiteSpace(user))
                    client.Credentials = new NetworkCredential(user, pass);

                var from = _cfg["Smtp:From"];
                var to = _cfg["Smtp:To"];
                using var msg = new MailMessage(from, to, subject, body);
                client.Send(msg);
            }
            catch { /* swallow – logging already handled elsewhere */ }
        }
    }

    public class TelegramNotifier : INotifier
    {
        private readonly IConfiguration _cfg;
        private static readonly HttpClient _http = new HttpClient();
        public TelegramNotifier(IConfiguration cfg) { _cfg = cfg; }

        public void Notify(string subject, string body, bool isSuccess)
        {
            if (!_cfg.GetValue<bool>("Telegram:Enabled")) return;

            try
            {
                var token = _cfg["Telegram:BotToken"];
                var chat = _cfg["Telegram:ChatId"];
                if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(chat)) return;

                var text = $"*RocketDrive*: {(isSuccess ? "✅ Success" : "❌ Failure")}\n*{subject}*\n{body}"
                    .Replace("&", "and"); // keep it simple

                var url = $"https://api.telegram.org/bot{token}/sendMessage";
                var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["chat_id"] = chat,
                    ["text"] = text,
                    ["parse_mode"] = "Markdown"
                });
                System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072; // TLS 1.2
                // fire-and-forget; no exception bubbling
                _ = _http.PostAsync(url, content);
            }
            catch { /* swallow */ }
        }
    }

    public class CompositeNotifier : INotifier
    {
        private readonly IEnumerable<INotifier> _notifiers;
        private readonly IConfiguration _cfg;
        public CompositeNotifier(IEnumerable<INotifier> notifiers, IConfiguration cfg)
        { _notifiers = notifiers; _cfg = cfg; }

        public void Notify(string subject, string body, bool isSuccess)
        {
            var onSuccess = _cfg.GetValue<bool>("BackupSettings:Notify:OnSuccess");
            var onFailure = _cfg.GetValue<bool>("BackupSettings:Notify:OnFailure");
            if ((isSuccess && !onSuccess) || (!isSuccess && !onFailure)) return;

            foreach (var n in _notifiers) n.Notify(subject, body, isSuccess);
        }
    }
}
