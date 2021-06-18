using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using GoldsparkIT.DnsBackend.Models;
using GoldsparkIT.DnsBackend.Responses.DnsBackend;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SQLite;

namespace GoldsparkIT.DnsBackend.Controllers
{
    [ApiController]
    [Route("dns")]
    public class DnsController : ControllerBase
    {
        private readonly SQLiteConnection _db;
        private readonly ILogger<DnsController> _logger;

        public DnsController(ILogger<DnsController> logger, SQLiteConnection db)
        {
            _logger = logger;
            _db = db;
        }

        [HttpGet("lookup/{qname}/{qtype}")]
        public ActionResult<IResponse> Lookup([FromRoute] string qname, [FromRoute] string qtype)
        {
            if (!EnsureLocalhost())
            {
                _logger.LogWarning($"DNS request from {Request?.HttpContext.Connection.RemoteIpAddress} denied; only localhost can query");
                return StatusCode(403);
            }

            if (qtype.Equals("soa", StringComparison.OrdinalIgnoreCase))
            {
                return ReturnSoaRecord(qname.TrimEnd('.'));
            }

            if (qtype.Equals("ns", StringComparison.OrdinalIgnoreCase))
            {
                return ReturnNsRecords(qname.TrimEnd('.'));
            }

            var records = _db.Table<DnsRecord>()?.ToList()?.Where(r => (qtype.Equals("any", StringComparison.OrdinalIgnoreCase) || r.Type.Equals(qtype, StringComparison.OrdinalIgnoreCase)) && CreateDnsRegex(r).Match(qname).Success);

            var responseRecords = records?.Select(r => new LookupRecord {qname = CreateFqdn(r), content = $"{(Utility.HasPriority(r.Type) ? $"{r.Priority} " : "")}{r.Content}", qtype = r.Type, ttl = r.Ttl, auth = 1}) ?? new List<LookupRecord>();

            if (qtype.Equals("any", StringComparison.OrdinalIgnoreCase))
            {
                var nsRecords = GetNsRecords(qname.TrimEnd('.'))?.Reverse();

                if (nsRecords != null && nsRecords.Any())
                {
                    responseRecords = nsRecords.Aggregate(responseRecords, (current, nsRecord) => current.Prepend(nsRecord));
                }

                var soaRecord = GetSoaRecord(qname.TrimEnd('.'));

                if (soaRecord != null)
                {
                    responseRecords = responseRecords.Prepend(soaRecord);
                }
            }

            if (responseRecords == null || !responseRecords.Any())
            {
                return new BaseResponse {result = false};
            }

            return new LookupResponse {result = responseRecords};
        }

        private ActionResult<IResponse> ReturnSoaRecord(string qname)
        {
            var soaRecord = GetSoaRecord(qname);

            if (soaRecord == null)
            {
                return new BaseResponse {result = false};
            }

            return new LookupResponse
            {
                result = new[]
                {
                    soaRecord
                }
            };
        }

        private ActionResult<IResponse> ReturnNsRecords(string qname)
        {
            var nsRecords = GetNsRecords(qname);

            if (nsRecords == null || !nsRecords.Any())
            {
                return new BaseResponse {result = false};
            }

            return new LookupResponse
            {
                result = nsRecords
            };
        }

        private IEnumerable<LookupRecord> GetNsRecords(string qname)
        {
            var domain = _db.Table<DnsDomain>().SingleOrDefault(x => x.Domain.Equals(qname, StringComparison.OrdinalIgnoreCase));

            if (domain == null)
            {
                return null;
            }

            var hosts = _db.Table<Node>().Select(n => n.Hostname);

            if (!hosts.Any())
            {
                hosts = new List<string> {_db.Table<InternalConfiguration>().Single().Hostname};
            }

            return hosts.Select(x => new LookupRecord
            {
                qname = domain.Domain,
                qtype = "NS",
                content = x,
                auth = 1,
                ttl = domain.Ttl
            });
        }

        private LookupRecord GetSoaRecord(string qname)
        {
            var domain = _db.Table<DnsDomain>().SingleOrDefault(x => x.Domain.Equals(qname, StringComparison.OrdinalIgnoreCase));

            if (domain == null)
            {
                return null;
            }

            var masterHost = _db.Table<Node>().SingleOrDefault(n => n.NodeId.Equals(_db.Table<Node>().Min(x => x.NodeId)))?.Hostname ?? _db.Table<InternalConfiguration>().Single().Hostname;

            return new LookupRecord
            {
                qname = domain.Domain,
                qtype = "SOA",
                content = $"{masterHost}. {domain.RName}. ({domain.Serial} {domain.Refresh} {domain.Retry} {domain.Expire} {domain.Ttl})",
                auth = 1,
                ttl = domain.Ttl
            };
        }

        private Regex CreateDnsRegex(DnsRecord record)
        {
            var regex = "^";

            if (record.Name.Equals("*"))
            {
                regex += $"(.*){Regex.Escape(".")}";
            }
            else if (!record.Name.Equals("@") && !string.IsNullOrWhiteSpace(record.Name))
            {
                regex += Regex.Escape($"{record.Name}.");
            }

            regex += $"{Regex.Escape($"{record.Domain}.")}$";

            return new Regex(regex);
        }

        private string CreateFqdn(DnsRecord record)
        {
            var result = "";

            if (!record.Name.Equals("@") && !string.IsNullOrWhiteSpace(record.Name))
            {
                result = $"{record.Name}.";
            }

            result += $"{record.Domain}";

            return result;
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