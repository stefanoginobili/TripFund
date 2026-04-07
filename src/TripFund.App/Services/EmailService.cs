using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel.Communication;

namespace TripFund.App.Services;

public class EmailService : IEmailService
{
    public async Task SendEmailAsync(string subject, string body, IEnumerable<string> recipients)
    {
        if (!Email.Default.IsComposeSupported)
        {
            throw new Exception("Nessuna applicazione email configurata su questo dispositivo.");
        }

        var message = new EmailMessage
        {
            Subject = subject,
            Body = body,
            BodyFormat = EmailBodyFormat.PlainText,
            To = new List<string>(recipients)
        };

        await Email.Default.ComposeAsync(message);
    }
}
