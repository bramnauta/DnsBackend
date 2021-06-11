using SQLite;

namespace GoldsparkIT.DnsBackend.Models
{
    public class BaseDnsDomain
    {
        [Indexed]
        public string Domain { get; set; }

        public string RName { get; set; }

        public int Refresh { get; set; }

        public int Retry { get; set; }

        public int Expire { get; set; }

        public int Ttl { get; set; }
    }
}