using System;
using SQLite;

namespace GoldsparkIT.DnsBackend.Models
{
    public class DnsDomain : BaseDnsDomain
    {
        public DnsDomain()
        {
        }

        public DnsDomain(BaseDnsDomain record)
        {
            Domain = record.Domain;
            RName = record.RName;
            Refresh = record.Refresh;
            Retry = record.Retry;
            Expire = record.Expire;
            Ttl = record.Ttl;
        }

        [PrimaryKey]
        public Guid Id { get; set; }

        public string Serial { get; set; }
    }
}