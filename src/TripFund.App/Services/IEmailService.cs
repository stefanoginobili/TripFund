using System.Collections.Generic;
using System.Threading.Tasks;

namespace TripFund.App.Services;

public interface IEmailService
{
    Task SendEmailAsync(string subject, string body, IEnumerable<string> recipients);
}
