using System.Collections.Generic;

namespace GoldsparkIT.DnsBackend.Common.Requests
{
    public class Request
    {
        public string Method { get; set; }
        public Dictionary<string, object> Parameters { get; set; }
    }
}