using System;
using System.Collections.Generic;
using System.Linq;
using GoldsparkIT.DnsBackend.Models;
using GoldsparkIT.DnsBackend.Responses.DnsBackend;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace GoldsparkIT.DnsBackend.Controllers
{
    [ApiController]
    [Route("dns")]
    public class DnsController : ControllerBase
    {
        private readonly ILogger<DnsController> _logger;

        public DnsController(ILogger<DnsController> logger)
        {
            _logger = logger;
        }

        [HttpGet("initialize")]
        public ActionResult<IResponse> Initialize()
        {
            if (!EnsureLocalhost())
            {
                _logger.LogWarning($"DNS request from {Request?.HttpContext.Connection.RemoteIpAddress} denied; only localhost can query");
                return StatusCode(403);
            }

            return new BaseResponse {result = true};
        }

        [HttpGet("lookup/{qname}/{qtype}")]
        public ActionResult<IResponse> Lookup([FromRoute] string qname, [FromRoute] string qtype)
        {
            if (!EnsureLocalhost())
            {
                _logger.LogWarning($"DNS request from {Request?.HttpContext.Connection.RemoteIpAddress} denied; only localhost can query");
                return StatusCode(403);
            }

            var db = DbProvider.ProvideSQLiteConnection(true);

            if (qtype.Equals("soa", StringComparison.OrdinalIgnoreCase))
            {
                var result = Utility.ReturnSoaRecord(qname.TrimEnd('.'), db);
                _logger.LogInformation($"{qtype} {qname}: {(result.Value is LookupResponse lookupResponse ? $"OK ({lookupResponse.result.Count()} results)" : "ERR")}");
                return result;
            }

            if (qtype.Equals("ns", StringComparison.OrdinalIgnoreCase))
            {
                var result = Utility.ReturnNsRecords(qname.TrimEnd('.'), db);
                _logger.LogInformation($"{qtype} {qname}: {(result.Value is LookupResponse lookupResponse ? $"OK ({lookupResponse.result.Count()} results)" : "ERR")}");
                return result;
            }

            var records = db.Table<DnsRecord>();

            if (!qtype.Equals("any", StringComparison.OrdinalIgnoreCase))
            {
                records = records.Where(x => qtype.ToLower() == x.Type.ToLower() || x.Type.ToLower() == "cname");
            }

            var reqName = qname.TrimEnd('.');

            records = records.Where(x => reqName.EndsWith(x.Domain, StringComparison.OrdinalIgnoreCase));

            var responseRecords = records.ToArray().Where(r => Utility.CreateDnsRegex(r).Match(qname).Success).Select(r => new LookupRecord {qname = Utility.CreateFqdn(r), content = $"{(Utility.HasPriority(r.Type) ? $"{r.Priority} " : "")}{r.Content}", qtype = r.Type, ttl = r.Ttl, auth = 1}) ?? new List<LookupRecord>();

            if (qtype.Equals("any", StringComparison.OrdinalIgnoreCase))
            {
                var nsRecords = Utility.GetNsRecords(qname.TrimEnd('.'), db)?.Reverse();

                if (nsRecords != null && nsRecords.Any())
                {
                    responseRecords = nsRecords.Aggregate(responseRecords, (current, nsRecord) => current.Prepend(nsRecord));
                }

                var soaRecord = Utility.GetSoaRecord(qname.TrimEnd('.'), db);

                if (soaRecord != null)
                {
                    responseRecords = responseRecords.Prepend(soaRecord);
                }
            }

            if (responseRecords == null || !responseRecords.Any())
            {
                _logger.LogInformation($"{qtype} {qname}: ERR");
                return new BaseResponse {result = false};
            }

            _logger.LogInformation($"{qtype} {qname}: OK ({responseRecords.Count()} results)");
            return new LookupResponse {result = responseRecords};
        }

        private bool EnsureLocalhost()
        {
            var remoteAddr = Request?.HttpContext.Connection.RemoteIpAddress;

            return (remoteAddr?.ToString().StartsWith("127.") ?? false) ||
                   (remoteAddr?.ToString().Equals("::1") ?? false) ||
                   (remoteAddr?.ToString().StartsWith("::ffff:127.") ?? false);
        }
    }
}