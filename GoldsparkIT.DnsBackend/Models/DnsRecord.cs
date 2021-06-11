using System;
using SQLite;

namespace GoldsparkIT.DnsBackend.Models
{
    public class DnsRecord : BaseDnsRecord
    {
        public DnsRecord()
        {
        }

        public DnsRecord(BaseDnsRecord record)
        {
            Domain = record.Domain;
            Name = record.Name;
            Type = record.Type;
            Content = record.Content;
            Ttl = record.Ttl;
            Priority = record.Priority;
        }

        [PrimaryKey]
        public Guid Id { get; set; }
    }
}