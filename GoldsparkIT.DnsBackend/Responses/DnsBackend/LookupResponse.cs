using System.Collections.Generic;

// ReSharper disable InconsistentNaming

namespace GoldsparkIT.DnsBackend.Responses.DnsBackend
{
    public class LookupResponse : IResponse
    {
        public IEnumerable<LookupRecord> result { get; set; }
    }
}