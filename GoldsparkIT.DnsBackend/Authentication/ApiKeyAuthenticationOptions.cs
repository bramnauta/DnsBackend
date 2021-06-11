using Microsoft.AspNetCore.Authentication;

namespace GoldsparkIT.DnsBackend.Authentication
{
    public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
    {
        public const string DefaultScheme = "API Key";
        public string AuthenticationType = DefaultScheme;
        public string Scheme => DefaultScheme;
    }
}