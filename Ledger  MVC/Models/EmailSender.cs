using System.Net.Mail;
using System.Net;
using Microsoft.AspNetCore.Identity.UI.Services;

namespace Ledger__MVC.Models
{

    public class EmailSender : IEmailSender
    {
        private readonly string _smtpServer;
        private readonly int _smtpPort;
        private readonly string _fromEmail;
        private readonly string _fromPassword;

        public EmailSender(IConfiguration configuration)
        {
            _smtpServer = configuration["SmtpSettings:Server"];
            _smtpPort = int.Parse(configuration["SmtpSettings:Port"]);
            _fromEmail = configuration["SmtpSettings:FromEmail"];
            _fromPassword = configuration["SmtpSettings:FromPassword"];
        }

        public async Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            try
            {
                // إعدادات SMTP
                var client = new SmtpClient(_smtpServer, _smtpPort)
                {
                    EnableSsl = true,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(_fromEmail, _fromPassword)
                };

                // إنشاء الرسالة
                var mailMessage = new MailMessage
                {
                    From = new MailAddress(_fromEmail),
                    Subject = subject,
                    Body = $"<html><body>{htmlMessage}</body></html>",
                    IsBodyHtml = true
                };

                mailMessage.To.Add(email);

                await client.SendMailAsync(mailMessage);
            }
            catch (SmtpException ex)
            {
                throw new Exception($"فشل إرسال البريد الإلكتروني: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                throw new Exception($"حدث خطأ أثناء إرسال البريد الإلكتروني: {ex.Message}", ex);
            }
        }
    }
}
