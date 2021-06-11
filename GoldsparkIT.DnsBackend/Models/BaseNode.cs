using SQLite;

namespace GoldsparkIT.DnsBackend.Models
{
    public class BaseNode
    {
        [Indexed]
        public string Hostname { get; set; }

        public int Port { get; set; }
    }
}