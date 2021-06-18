using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using GoldsparkIT.DnsBackend.Models;
using GoldsparkIT.DnsBackend.Requests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using RestSharp;
using SQLite;

namespace GoldsparkIT.DnsBackend.Controllers
{
    [ApiController]
    [Route("mgmt")]
    public class ManagementController : ControllerBase
    {
        private readonly IRestClient _client;
        private readonly SQLiteConnection _db;
        private readonly ILogger<ManagementController> _logger;

        public ManagementController(ILogger<ManagementController> logger, SQLiteConnection db, IRestClient client)
        {
            _logger = logger;
            _db = db;
            _client = client;
        }

        #region Domains

        [Authorize(Roles = "UserKey")]
        [HttpGet("domain")]
        [ProducesResponseType(typeof(DnsDomain[]), (int) HttpStatusCode.OK)]
        [ProducesResponseType((int) HttpStatusCode.NotFound)]
        [ProducesResponseType((int) HttpStatusCode.Unauthorized)]
        [ProducesResponseType((int) HttpStatusCode.Forbidden)]
        public ActionResult GetDomains()
        {
            var domains = _db.Table<DnsDomain>();

            return domains.Any() ? Ok(domains) : NotFound();
        }

        [Authorize(Roles = "UserKey")]
        [HttpGet("domain/{id}")]
        [ProducesResponseType(typeof(DnsDomain), (int) HttpStatusCode.OK)]
        [ProducesResponseType((int) HttpStatusCode.BadRequest)]
        [ProducesResponseType((int) HttpStatusCode.NotFound)]
        [ProducesResponseType((int) HttpStatusCode.Unauthorized)]
        [ProducesResponseType((int) HttpStatusCode.Forbidden)]
        public ActionResult GetDomain([FromRoute] string id)
        {
            if (!Guid.TryParse(id, out var idValue))
            {
                return BadRequest();
            }

            var domain = _db.Table<DnsDomain>().SingleOrDefault(r => r.Id == idValue);

            return domain == null ? NotFound() : Ok(domain);
        }

        [Authorize(Roles = "UserKey")]
        [HttpGet("domain/find/{domain}")]
        [ProducesResponseType(typeof(DnsDomain), (int) HttpStatusCode.OK)]
        [ProducesResponseType((int) HttpStatusCode.NotFound)]
        [ProducesResponseType((int) HttpStatusCode.Unauthorized)]
        [ProducesResponseType((int) HttpStatusCode.Forbidden)]
        public ActionResult FindDomain([FromRoute] string domain)
        {
            var domainObj = _db.Table<DnsDomain>().ToList().SingleOrDefault(r => r.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase));

            return domainObj != null ? Ok(domainObj) : NotFound();
        }

        [Authorize(Roles = "UserKey")]
        [HttpPost("domain")]
        [ProducesResponseType(typeof(DnsDomain), (int) HttpStatusCode.Created)]
        [ProducesResponseType((int) HttpStatusCode.Unauthorized)]
        [ProducesResponseType((int) HttpStatusCode.Forbidden)]
        [ProducesResponseType((int) HttpStatusCode.InternalServerError)]
        public ActionResult AddDomain([FromBody] BaseDnsDomain body)
        {
            var domainObj = _db.Table<DnsDomain>().ToList().SingleOrDefault(r => r.Domain.Equals(body.Domain, StringComparison.OrdinalIgnoreCase));

            if (domainObj != null)
            {
                return Conflict("Domain already exists");
            }

            var newDomain = new DnsDomain(body) {Id = Guid.NewGuid(), Serial = $"{DateTimeOffset.Now:yyyyMMdd}00", LastChanged = DateTimeOffset.Now.ToUniversalTime()};

            if (_db.Insert(newDomain) > 0)
            {
                Synchronizer.Get().Send(newDomain, NotifyTableChangedAction.Insert, _db, _logger);
                return Created($"mgmt/domain/{newDomain.Id}", newDomain);
            }

            return StatusCode((int) HttpStatusCode.InternalServerError);
        }

        [Authorize(Roles = "UserKey")]
        [HttpPut("domain/{id}")]
        [ProducesResponseType(typeof(DnsDomain), (int) HttpStatusCode.OK)]
        [ProducesResponseType((int) HttpStatusCode.BadRequest)]
        [ProducesResponseType((int) HttpStatusCode.NotFound)]
        [ProducesResponseType((int) HttpStatusCode.Unauthorized)]
        [ProducesResponseType((int) HttpStatusCode.Forbidden)]
        [ProducesResponseType((int) HttpStatusCode.InternalServerError)]
        public ActionResult UpdateDomain([FromRoute] string id, [FromBody] BaseDnsDomain body)
        {
            if (!Guid.TryParse(id, out var idValue))
            {
                return BadRequest();
            }

            var domainObj = _db.Table<DnsDomain>().SingleOrDefault(r => r.Id == idValue);

            if (domainObj == null)
            {
                return NotFound();
            }

            var syncObjects = new List<object>();

            var oldDomain = domainObj.Domain;

            domainObj.Domain = body.Domain;
            domainObj.Expire = body.Expire;
            domainObj.RName = body.RName;
            domainObj.Refresh = body.Refresh;
            domainObj.Retry = body.Retry;
            domainObj.Ttl = body.Ttl;
            domainObj.Serial = GetIncreasedSerial(domainObj);
            domainObj.LastChanged = DateTimeOffset.Now.ToUniversalTime();

            _db.BeginTransaction();

            try
            {
                if (!oldDomain.Equals(body.Domain))
                {
                    var updateRows = _db.Table<DnsRecord>().ToList().Where(r => r.Domain.Equals(oldDomain, StringComparison.OrdinalIgnoreCase)).Select(r =>
                    {
                        r.Domain = body.Domain;
                        return r;
                    }).ToList();

                    _db.UpdateAll(updateRows);

                    syncObjects.AddRange(updateRows);
                }

                var affectedCount = _db.Update(domainObj);

                syncObjects.Add(domainObj);

                _db.Commit();

                if (affectedCount <= 0)
                {
                    return StatusCode((int) HttpStatusCode.InternalServerError);
                }

                var synchronizer = Synchronizer.Get();

                foreach (var item in syncObjects)
                {
                    synchronizer.Send(item, NotifyTableChangedAction.Update, _db, _logger);
                }

                return Ok(domainObj);
            }
            catch (Exception ex)
            {
                _db.Rollback();
                _logger.LogError(ex, ex.Message);
                return StatusCode((int) HttpStatusCode.InternalServerError);
            }
        }

        [Authorize(Roles = "UserKey")]
        [HttpDelete("domain/{id}")]
        [ProducesResponseType((int) HttpStatusCode.OK)]
        [ProducesResponseType((int) HttpStatusCode.BadRequest)]
        [ProducesResponseType((int) HttpStatusCode.NotFound)]
        [ProducesResponseType((int) HttpStatusCode.Unauthorized)]
        [ProducesResponseType((int) HttpStatusCode.Forbidden)]
        [ProducesResponseType((int) HttpStatusCode.InternalServerError)]
        public ActionResult DeleteDomain([FromRoute] string id, [FromQuery] bool force = false)
        {
            if (!Guid.TryParse(id, out var idValue))
            {
                return BadRequest();
            }

            var domainObj = _db.Table<DnsDomain>().SingleOrDefault(r => r.Id == idValue);

            if (domainObj == null)
            {
                return NotFound();
            }

            var relatedRecords = _db.Table<DnsRecord>().ToList().Where(r => domainObj.Domain.Equals(r.Domain, StringComparison.OrdinalIgnoreCase)).ToList();

            var syncObjects = new List<object>();

            syncObjects.AddRange(relatedRecords);

            _db.BeginTransaction();

            try
            {
                if (relatedRecords.Any())
                {
                    if (!force)
                    {
                        _db.Rollback();
                        return Conflict();
                    }

                    foreach (var relatedRecord in relatedRecords)
                    {
                        _db.Delete(relatedRecord);
                    }
                }

                var affectedCount = _db.Delete(domainObj);

                syncObjects.Add(domainObj);

                _db.Commit();

                if (affectedCount <= 0)
                {
                    return StatusCode((int) HttpStatusCode.InternalServerError);
                }

                var synchronizer = Synchronizer.Get();

                foreach (var item in syncObjects)
                {
                    synchronizer.Send(item, NotifyTableChangedAction.Delete, _db, _logger);
                }

                return Ok();
            }
            catch (Exception ex)
            {
                _db.Rollback();
                _logger.LogError(ex, ex.Message);
                return StatusCode((int) HttpStatusCode.InternalServerError);
            }
        }

        #endregion

        #region Records

        [Authorize(Roles = "UserKey")]
        [HttpGet("record")]
        [ProducesResponseType(typeof(DnsRecord[]), (int) HttpStatusCode.OK)]
        [ProducesResponseType((int) HttpStatusCode.NotFound)]
        [ProducesResponseType((int) HttpStatusCode.Unauthorized)]
        [ProducesResponseType((int) HttpStatusCode.Forbidden)]
        public ActionResult GetRecords()
        {
            var records = _db.Table<DnsRecord>();

            return records.Any() ? Ok(records) : NotFound();
        }

        [Authorize(Roles = "UserKey")]
        [HttpGet("record/{id}")]
        [ProducesResponseType(typeof(DnsRecord), (int) HttpStatusCode.OK)]
        [ProducesResponseType((int) HttpStatusCode.BadRequest)]
        [ProducesResponseType((int) HttpStatusCode.NotFound)]
        [ProducesResponseType((int) HttpStatusCode.Unauthorized)]
        [ProducesResponseType((int) HttpStatusCode.Forbidden)]
        public ActionResult GetRecord([FromRoute] string id)
        {
            if (!Guid.TryParse(id, out var idValue))
            {
                return BadRequest();
            }

            var record = _db.Table<DnsRecord>().SingleOrDefault(r => r.Id == idValue);

            return record == null ? NotFound() : Ok(record);
        }

        [Authorize(Roles = "UserKey")]
        [HttpGet("record/find/{domain}")]
        [ProducesResponseType(typeof(DnsRecord[]), (int) HttpStatusCode.OK)]
        [ProducesResponseType((int) HttpStatusCode.NotFound)]
        [ProducesResponseType((int) HttpStatusCode.Unauthorized)]
        [ProducesResponseType((int) HttpStatusCode.Forbidden)]
        public ActionResult GetRecords([FromRoute] string domain)
        {
            var records = _db.Table<DnsRecord>().ToList().Where(r => r.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase));

            return records.Any() ? Ok(records) : NotFound();
        }

        [Authorize(Roles = "UserKey")]
        [HttpGet("record/find/{domain}/{type}")]
        [ProducesResponseType(typeof(DnsRecord[]), (int) HttpStatusCode.OK)]
        [ProducesResponseType((int) HttpStatusCode.NotFound)]
        [ProducesResponseType((int) HttpStatusCode.Unauthorized)]
        [ProducesResponseType((int) HttpStatusCode.Forbidden)]
        public ActionResult GetRecords([FromRoute] string domain, [FromRoute] string type)
        {
            if (type.Equals("soa", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest("SOA records are automatically maintained and cannot be read from this API");
            }

            if (type.Equals("ns", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest("NS records are automatically maintained and cannot be read from this API");
            }

            var records = _db.Table<DnsRecord>().ToList().Where(r => r.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase) && (type.Equals("any", StringComparison.OrdinalIgnoreCase) || type.Equals(r.Type, StringComparison.OrdinalIgnoreCase)));

            return records.Any() ? Ok(records) : NotFound();
        }

        [Authorize(Roles = "UserKey")]
        [HttpPost("record")]
        [ProducesResponseType(typeof(DnsRecord), (int) HttpStatusCode.Created)]
        [ProducesResponseType((int) HttpStatusCode.NotFound)]
        [ProducesResponseType((int) HttpStatusCode.Unauthorized)]
        [ProducesResponseType((int) HttpStatusCode.Forbidden)]
        [ProducesResponseType((int) HttpStatusCode.InternalServerError)]
        public ActionResult AddRecord([FromBody] BaseDnsRecord body)
        {
            if (body.Type.Equals("soa", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest("SOA records are automatically maintained and cannot be manually added");
            }

            if (body.Type.Equals("ns", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest("NS records are automatically maintained and cannot be manually added");
            }

            if (!_db.Table<DnsDomain>().ToList().Any(d => d.Domain.Equals(body.Domain, StringComparison.OrdinalIgnoreCase)))
            {
                return NotFound();
            }

            var newRecord = new DnsRecord(body) {Id = Guid.NewGuid(), LastChanged = DateTimeOffset.Now.ToUniversalTime()};

            if (!Utility.HasPriority(newRecord.Type))
            {
                newRecord.Priority = null;
            }
            else
            {
                newRecord.Priority ??= 0;
            }

            if (_db.Insert(newRecord) <= 0)
            {
                return StatusCode((int) HttpStatusCode.InternalServerError);
            }

            Synchronizer.Get().Send(newRecord, NotifyTableChangedAction.Insert, _db, _logger);

            IncreaseSerial(newRecord.Domain);

            return Created($"mgmt/record/{newRecord.Id}", newRecord);
        }

        [Authorize(Roles = "UserKey")]
        [HttpPut("record/{id}")]
        [ProducesResponseType(typeof(DnsRecord), (int) HttpStatusCode.OK)]
        [ProducesResponseType((int) HttpStatusCode.BadRequest)]
        [ProducesResponseType((int) HttpStatusCode.NotFound)]
        [ProducesResponseType((int) HttpStatusCode.Unauthorized)]
        [ProducesResponseType((int) HttpStatusCode.Forbidden)]
        [ProducesResponseType((int) HttpStatusCode.InternalServerError)]
        public ActionResult UpdateRecord([FromRoute] string id, [FromBody] BaseDnsRecord body)
        {
            if (!Guid.TryParse(id, out var idValue))
            {
                return BadRequest();
            }

            if (body.Type.Equals("soa", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest("SOA records are automatically maintained and cannot be manually updated");
            }

            if (body.Type.Equals("ns", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest("NS records are automatically maintained and cannot be manually updated");
            }

            if (!_db.Table<DnsDomain>().ToList().Any(d => d.Domain.Equals(body.Domain, StringComparison.OrdinalIgnoreCase)))
            {
                return NotFound();
            }

            var record = _db.Table<DnsRecord>().SingleOrDefault(r => r.Id == idValue);

            if (record == null)
            {
                return NotFound();
            }

            record.Domain = body.Domain;
            record.Name = body.Name;
            record.Type = body.Type;
            record.Content = body.Content;
            record.Ttl = body.Ttl;
            record.Priority = Utility.HasPriority(body.Type) ? body.Priority ?? 0 : null;
            record.LastChanged = DateTimeOffset.Now.ToUniversalTime();

            if (_db.Update(record) <= 0)
            {
                return StatusCode((int) HttpStatusCode.InternalServerError);
            }

            Synchronizer.Get().Send(record, NotifyTableChangedAction.Update, _db, _logger);

            IncreaseSerial(record.Domain);

            return Ok(record);
        }

        [Authorize(Roles = "UserKey")]
        [HttpDelete("record/{id}")]
        [ProducesResponseType((int) HttpStatusCode.OK)]
        [ProducesResponseType((int) HttpStatusCode.BadRequest)]
        [ProducesResponseType((int) HttpStatusCode.NotFound)]
        [ProducesResponseType((int) HttpStatusCode.Unauthorized)]
        [ProducesResponseType((int) HttpStatusCode.Forbidden)]
        [ProducesResponseType((int) HttpStatusCode.InternalServerError)]
        public ActionResult DeleteRecord([FromRoute] string id)
        {
            if (!Guid.TryParse(id, out var idValue))
            {
                return BadRequest();
            }

            var record = _db.Table<DnsRecord>().SingleOrDefault(r => r.Id == idValue);

            if (record == null)
            {
                return NotFound();
            }

            if (_db.Delete(record) <= 0)
            {
                return StatusCode((int) HttpStatusCode.InternalServerError);
            }

            Synchronizer.Get().Send(record, NotifyTableChangedAction.Delete, _db, _logger);

            IncreaseSerial(record.Domain);

            return Ok();
        }

        #endregion

        #region Nodes

        [Authorize(Roles = "UserKey")]
        [HttpGet("node")]
        [ProducesResponseType(typeof(Node[]), (int) HttpStatusCode.OK)]
        [ProducesResponseType((int) HttpStatusCode.NotFound)]
        [ProducesResponseType((int) HttpStatusCode.Unauthorized)]
        [ProducesResponseType((int) HttpStatusCode.Forbidden)]
        public ActionResult GetNodes()
        {
            var nodes = _db.Table<Node>();

            return nodes.Any()
                ? Ok(nodes)
                : NotFound();
        }

        [Authorize(Roles = "UserKey")]
        [HttpGet("node/{id}")]
        [ProducesResponseType(typeof(Node), (int) HttpStatusCode.OK)]
        [ProducesResponseType((int) HttpStatusCode.BadRequest)]
        [ProducesResponseType((int) HttpStatusCode.NotFound)]
        [ProducesResponseType((int) HttpStatusCode.Unauthorized)]
        [ProducesResponseType((int) HttpStatusCode.Forbidden)]
        public ActionResult GetNode([FromRoute] string id)
        {
            if (!Guid.TryParse(id, out var idValue))
            {
                return BadRequest();
            }

            var node = _db.Table<Node>().SingleOrDefault(r => r.Id == idValue);

            return node == null ? NotFound() : Ok(node);
        }

        [Authorize(Roles = "UserKey")]
        [HttpGet("node/find/{hostName}")]
        [ProducesResponseType(typeof(Node), (int) HttpStatusCode.OK)]
        [ProducesResponseType((int) HttpStatusCode.NotFound)]
        [ProducesResponseType((int) HttpStatusCode.Unauthorized)]
        [ProducesResponseType((int) HttpStatusCode.Forbidden)]
        public ActionResult FindNode([FromRoute] string hostName)
        {
            var nodeObj = _db.Table<Node>().ToList().SingleOrDefault(r => r.Hostname.Equals(hostName, StringComparison.OrdinalIgnoreCase));

            return nodeObj != null ? Ok(nodeObj) : NotFound();
        }

        [Authorize(Roles = "UserKey")]
        [HttpPost("node")]
        [ProducesResponseType((int) HttpStatusCode.Accepted)]
        [ProducesResponseType((int) HttpStatusCode.Unauthorized)]
        [ProducesResponseType((int) HttpStatusCode.Forbidden)]
        [ProducesResponseType((int) HttpStatusCode.InternalServerError)]
        public ActionResult AddNode([FromBody] AddNodeRequest body)
        {
            var apiKey = _db.Table<ApiKey>().First(k => k.ClusterKey).Key;

            var req = new RestRequest($"http://{body.Hostname}:{body.Port}/sync/hostname");

            var hostnameResponse = _client.Get(req);

            if (!hostnameResponse.IsSuccessful)
            {
                return StatusCode((int) HttpStatusCode.InternalServerError, $"Response from node does not indicate success: {(int) hostnameResponse.StatusCode} {hostnameResponse.StatusDescription}\r\nContent: {hostnameResponse.Content}");
            }

            var hostname = hostnameResponse.Content;

            req = new RestRequest($"http://{hostname}:{body.Port}/sync/cluster");

            var configuration = _db.Table<InternalConfiguration>().Single();

            req.AddHeader("X-Api-Key", body.ApiKey);
            req.AddJsonBody(new
            {
                hostname = configuration.Hostname,
                port = configuration.Port,
                apiKey
            });

            var response = _client.Post(req);

            return response.IsSuccessful ? Accepted() : StatusCode((int) HttpStatusCode.InternalServerError, $"Response from node does not indicate success: {(int) response.StatusCode} {response.StatusDescription}\r\nContent: {response.Content}");
        }

        [Authorize(Roles = "UserKey")]
        [HttpPut("node/{id}")]
        [ProducesResponseType(typeof(DnsDomain), (int) HttpStatusCode.OK)]
        [ProducesResponseType((int) HttpStatusCode.BadRequest)]
        [ProducesResponseType((int) HttpStatusCode.NotFound)]
        [ProducesResponseType((int) HttpStatusCode.Unauthorized)]
        [ProducesResponseType((int) HttpStatusCode.Forbidden)]
        [ProducesResponseType((int) HttpStatusCode.InternalServerError)]
        public ActionResult UpdateNode([FromRoute] string id, [FromBody] BaseNode body)
        {
            if (!Guid.TryParse(id, out var idValue))
            {
                return BadRequest();
            }

            var nodeObj = _db.Table<Node>().SingleOrDefault(r => r.Id == idValue);

            if (nodeObj == null)
            {
                return NotFound();
            }

            nodeObj.Hostname = body.Hostname;
            nodeObj.LastChanged = DateTimeOffset.Now.ToUniversalTime();

            try
            {
                var affectedCount = _db.Update(nodeObj);

                if (affectedCount <= 0)
                {
                    return StatusCode((int) HttpStatusCode.InternalServerError);
                }

                Synchronizer.Get().Send(nodeObj, NotifyTableChangedAction.Update, _db, _logger);

                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, ex.Message);
                return StatusCode((int) HttpStatusCode.InternalServerError);
            }
        }

        [Authorize(Roles = "UserKey")]
        [HttpDelete("node/{id}")]
        [ProducesResponseType((int) HttpStatusCode.OK)]
        [ProducesResponseType((int) HttpStatusCode.BadRequest)]
        [ProducesResponseType((int) HttpStatusCode.NotFound)]
        [ProducesResponseType((int) HttpStatusCode.Unauthorized)]
        [ProducesResponseType((int) HttpStatusCode.Forbidden)]
        [ProducesResponseType((int) HttpStatusCode.InternalServerError)]
        public ActionResult DeleteNode([FromRoute] string id)
        {
            if (!Guid.TryParse(id, out var idValue))
            {
                return BadRequest();
            }

            var nodeObj = _db.Table<Node>().SingleOrDefault(r => r.Id == idValue);

            if (nodeObj == null)
            {
                return NotFound();
            }

            try
            {
                var affectedCount = _db.Delete(nodeObj);

                if (affectedCount <= 0)
                {
                    return StatusCode((int) HttpStatusCode.InternalServerError);
                }

                Synchronizer.Get().Send(nodeObj, NotifyTableChangedAction.Delete, _db, _logger);

                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, ex.Message);
                return StatusCode((int) HttpStatusCode.InternalServerError);
            }
        }

        #endregion

        #region Configuration

        [Authorize(Roles = "UserKey")]
        [HttpGet("hostName")]
        [ProducesResponseType((int) HttpStatusCode.OK)]
        [ProducesResponseType((int) HttpStatusCode.Unauthorized)]
        [ProducesResponseType((int) HttpStatusCode.Forbidden)]
        [ProducesResponseType((int) HttpStatusCode.InternalServerError)]
        public ActionResult GetHostname()
        {
            var configurationObj = _db.Table<InternalConfiguration>().Single();

            return Ok(configurationObj.Hostname);
        }

        [Authorize(Roles = "UserKey")]
        [HttpPut("hostName")]
        [ProducesResponseType((int) HttpStatusCode.OK)]
        [ProducesResponseType((int) HttpStatusCode.BadRequest)]
        [ProducesResponseType((int) HttpStatusCode.NotFound)]
        [ProducesResponseType((int) HttpStatusCode.Unauthorized)]
        [ProducesResponseType((int) HttpStatusCode.Forbidden)]
        [ProducesResponseType((int) HttpStatusCode.InternalServerError)]
        public ActionResult UpdateHostname([FromBody] UpdateHostnameRequest body)
        {
            var configurationObj = _db.Table<InternalConfiguration>().Single();

            configurationObj.Hostname = body.Hostname;

            var affectedCount = _db.Update(configurationObj);

            if (affectedCount < 1)
            {
                return StatusCode((int) HttpStatusCode.InternalServerError);
            }

            var nodeObj = _db.Table<Node>().Single(n => n.NodeId == configurationObj.NodeId);

            nodeObj.Hostname = body.Hostname;
            nodeObj.LastChanged = DateTimeOffset.Now.ToUniversalTime();

            affectedCount = _db.Update(nodeObj);

            if (affectedCount < 1)
            {
                return StatusCode((int) HttpStatusCode.InternalServerError);
            }

            Synchronizer.Get().Send(nodeObj, NotifyTableChangedAction.Update, _db, _logger);

            return Ok();
        }

        #endregion

        #region Utility

        private string GetIncreasedSerial(DnsDomain domain)
        {
            if (domain == null)
            {
                return null;
            }

            var countOfChange = 0;

            if (domain.Serial.StartsWith($"{DateTimeOffset.Now:yyyyMMdd}"))
            {
                countOfChange = int.Parse(domain.Serial.Substring(domain.Serial.Length - 2, 2)) + 1;
            }

            var oldSerial = long.Parse(domain.Serial);
            long newSerial = 0;

            if (countOfChange < 100)
            {
                newSerial = long.Parse($"{DateTimeOffset.Now:yyyyMMdd}{countOfChange:00}");
            }

            if (oldSerial >= newSerial)
            {
                newSerial = oldSerial + 1;
            }

            return $"{newSerial:0000000000}";
        }

        private void IncreaseSerial(string domainName)
        {
            var domain = _db.Table<DnsDomain>().SingleOrDefault(r => r.Domain.Equals(domainName, StringComparison.OrdinalIgnoreCase));

            if (domain == null)
            {
                return;
            }

            domain.Serial = GetIncreasedSerial(domain);
            domain.LastChanged = DateTimeOffset.Now.ToUniversalTime();

            _db.Update(domain);

            Synchronizer.Get().Send(domain, NotifyTableChangedAction.Update, _db, _logger);
        }

        #endregion
    }
}