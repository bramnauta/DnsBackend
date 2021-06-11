using System;
using SQLite;

namespace GoldsparkIT.DnsBackend.Models
{
    public class InternalConfiguration
    {
        [PrimaryKey]
        public Guid Id { get; set; }

        public string Hostname { get; set; }

        public Guid NodeId { get; set; }

        public int Port { get; set; }
    }
}