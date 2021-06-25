using System;
using System.Collections.Generic;
using System.Linq;
using GoldsparkIT.DnsBackend.Common.Requests;
using GoldsparkIT.DnsBackend.Common.Responses;
using GoldsparkIT.DnsBackend.Models;
using GoldsparkIT.DnsBackend.Responses.DnsBackend;
using Newtonsoft.Json;
using SQLite;

namespace GoldsparkIT.DnsBackend
{
    public abstract class JsonResponder
    {
        protected readonly SQLiteConnection Db;

        protected JsonResponder(SQLiteConnection db)
        {
            Db = db;
        }

        protected abstract Response Initialize(Dictionary<string, object> parameters);

        protected abstract void Send(Response data, object state);

        protected void ProcessMessage(string msg, object state)
        {
            try
            {
                var data = JsonConvert.DeserializeObject<Request>(msg);

                ProcessMessage(data, state);
            }
            catch
            {
                Send(Response.False, state);
            }
        }

        protected void ProcessMessage(Request data, object state)
        {
            try
            {
                Response response;

                switch (data.Method.ToLower())
                {
                    case "initialize":
                        response = Initialize(data.Parameters);

                        Send(response, state);

                        break;
                    case "lookup":
                        response = Lookup(data.Parameters);

                        Send(response, state);

                        break;
                    default:
                        Send(Response.False, state);
                        break;
                }
            }
            catch
            {
                Send(Response.False, state);
            }
        }

        private Response Lookup(Dictionary<string, object> parameters)
        {
            var qtype = parameters.GetAs<string>("qtype");
            var qname = parameters.GetAs<string>("qname").TrimEnd('.');

            if (qtype.Equals("soa", StringComparison.OrdinalIgnoreCase))
            {
                var soaRecord = Utility.GetSoaRecord(qname, Db);

                return soaRecord == null ? Response.False : new Response(new List<LookupRecord> {soaRecord});
            }

            if (qtype.Equals("ns", StringComparison.OrdinalIgnoreCase))
            {
                var nsRecords = Utility.GetNsRecords(qname, Db);

                return (nsRecords == null) | !nsRecords.Any() ? Response.False : new Response(nsRecords);
            }

            var records = Db.Table<DnsRecord>();

            if (!qtype.Equals("any", StringComparison.OrdinalIgnoreCase))
            {
                records = records.Where(x => string.Equals(qtype, x.Type, StringComparison.CurrentCultureIgnoreCase));
            }

            records = records.Where(x => qname.EndsWith(x.Domain, StringComparison.OrdinalIgnoreCase));

            var responseRecords = records.ToArray().Where(r => Utility.CreateDnsRegex(r).Match(qname).Success).Select(r => new LookupRecord {qname = Utility.CreateFqdn(r), content = $"{(Utility.HasPriority(r.Type) ? $"{r.Priority} " : "")}{r.Content}", qtype = r.Type, ttl = r.Ttl, auth = 1}) ?? new List<LookupRecord>();

            if (qtype.Equals("any", StringComparison.OrdinalIgnoreCase))
            {
                var nsRecords = Utility.GetNsRecords(qname.TrimEnd('.'), Db)?.Reverse();

                if (nsRecords != null && nsRecords.Any())
                {
                    responseRecords = nsRecords.Aggregate(responseRecords, (current, nsRecord) => current.Prepend(nsRecord));
                }

                var soaRecord = Utility.GetSoaRecord(qname.TrimEnd('.'), Db);

                if (soaRecord != null)
                {
                    responseRecords = responseRecords.Prepend(soaRecord);
                }
            }

            if (responseRecords == null || !responseRecords.Any())
            {
                return Response.False;
            }

            return new Response(responseRecords);
        }
    }
}