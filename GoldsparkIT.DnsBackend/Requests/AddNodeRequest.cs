using GoldsparkIT.DnsBackend.Models;

namespace GoldsparkIT.DnsBackend.Requests
{
    public class AddNodeRequest : BaseNode
    {
        public string ApiKey { get; set; }
    }
}