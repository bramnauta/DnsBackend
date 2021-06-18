using System.Collections.Generic;
using GoldsparkIT.DnsBackend.Models;

namespace GoldsparkIT.DnsBackend.Responses
{
    public class SyncMessage
    {
        public IEnumerable<ApiKey> ApiKeys { get; init; }
        public IEnumerable<DnsDomain> DnsDomains { get; init; }
        public IEnumerable<DnsRecord> DnsRecords { get; init; }
        public IEnumerable<Node> Nodes { get; init; }
        public int MessageVersion => GetLocalMessageVersion();

        public static int GetLocalMessageVersion()
        {
            return 1;
        }
    }
}