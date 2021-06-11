using System;
using SQLite;

namespace GoldsparkIT.DnsBackend.Models
{
    public class ApiKey
    {
        [PrimaryKey]
        public Guid Id { get; set; }

        public string Name { get; set; }

        [Indexed]
        public string Key { get; set; }

        public bool ClusterKey { get; set; }
    }
}