using System.Threading.Tasks;
using GoldsparkIT.DnsBackend.Models;

namespace GoldsparkIT.DnsBackend.Authentication
{
    public interface IApiKeyProvider
    {
        Task<ApiKey> Execute(string providedApiKey);
    }
}