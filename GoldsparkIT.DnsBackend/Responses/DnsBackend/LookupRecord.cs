// ReSharper disable InconsistentNaming

namespace GoldsparkIT.DnsBackend.Responses.DnsBackend
{
    public class LookupRecord
    {
        public string qtype { get; set; }
        public string qname { get; set; }
        public string content { get; set; }
        public long ttl { get; set; }
        public byte auth { get; set; }
    }
}