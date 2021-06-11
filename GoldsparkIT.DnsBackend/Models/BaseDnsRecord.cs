using SQLite;

namespace GoldsparkIT.DnsBackend.Models
{
    public class BaseDnsRecord
    {
        [Indexed]
        public string Domain { get; set; }

        [Indexed]
        public string Name { get; set; }

        [Indexed]
        public string Type { get; set; }

        public string Content { get; set; }
        public long Ttl { get; set; }
        public long? Priority { get; set; }
    }
}