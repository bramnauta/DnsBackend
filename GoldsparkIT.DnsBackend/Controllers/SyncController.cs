using System;
using System.Linq;
using System.Net;
using GoldsparkIT.DnsBackend.Models;
using GoldsparkIT.DnsBackend.Requests;
using GoldsparkIT.DnsBackend.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using RestSharp;
using SQLite;

namespace GoldsparkIT.DnsBackend.Controllers
{
    [ApiController]
    [Route("sync")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public class SyncController : ControllerBase
    {
        private readonly IRestClient _client;
        private readonly SQLiteConnection _db;
        private readonly ILogger<SyncController> _logger;

        public SyncController(ILogger<SyncController> logger, SQLiteConnection db, IRestClient client)
        {
            _logger = logger;
            _db = db;
            _client = client;
        }

        [HttpGet("getDb")]
        [ProducesResponseType(typeof(byte[]), (int) HttpStatusCode.OK)]
        [ProducesResponseType((int) HttpStatusCode.Unauthorized)]
        [ProducesResponseType((int) HttpStatusCode.Forbidden)]
        [Authorize(Roles = "ClusterKey")]
        public ActionResult GetDb()
        {
            var remoteAddr = Request?.HttpContext.Connection.RemoteIpAddress;

            _logger.LogInformation($"Received database request from {remoteAddr}");

            var response = new SyncMessage {ApiKeys = _db.Table<ApiKey>(), DnsDomains = _db.Table<DnsDomain>(), DnsRecords = _db.Table<DnsRecord>(), Nodes = _db.Table<Node>()};

            return Ok(response);
        }

        [HttpGet("nodeId")]
        [ProducesResponseType(typeof(Guid), (int) HttpStatusCode.OK)]
        [ProducesResponseType((int) HttpStatusCode.Unauthorized)]
        [ProducesResponseType((int) HttpStatusCode.Forbidden)]
        [Authorize]
        public ActionResult GetNodeId()
        {
            return Content(_db.Table<InternalConfiguration>().Single().NodeId.ToString(), "text/plain");
        }

        [HttpGet("hostname")]
        [ProducesResponseType(typeof(string), (int) HttpStatusCode.OK)]
        [ProducesResponseType((int) HttpStatusCode.Unauthorized)]
        [ProducesResponseType((int) HttpStatusCode.Forbidden)]
        [Authorize]
        public ActionResult GetHostname()
        {
            return Content(_db.Table<InternalConfiguration>().Single().Hostname, "text/plain");
        }

        [HttpPut("event")]
        [ProducesResponseType((int) HttpStatusCode.OK)]
        [ProducesResponseType((int) HttpStatusCode.Unauthorized)]
        [ProducesResponseType((int) HttpStatusCode.Forbidden)]
        [Authorize(Roles = "ClusterKey")]
        public ActionResult Event([FromBody] EventRequest body)
        {
            switch (body.Type)
            {
                case "Node":
                    var nodeObj = JsonConvert.DeserializeObject<Node>(body.Data);

                    var configuration = _db.Table<InternalConfiguration>().Single();

                    return nodeObj.NodeId.Equals(configuration.NodeId) ? Ok() : ExecuteEvent(body, nodeObj);
                case "DnsDomain":
                    return ExecuteEvent(body, JsonConvert.DeserializeObject<DnsDomain>(body.Data));
                case "DnsRecord":
                    return ExecuteEvent(body, JsonConvert.DeserializeObject<DnsRecord>(body.Data));
                case "ApiKey":
                    return ExecuteEvent(body, JsonConvert.DeserializeObject<ApiKey>(body.Data));
                default:
                    return BadRequest();
            }
        }

        private ActionResult ExecuteEvent(EventRequest body, object obj)
        {
            _logger.LogInformation($"Received sync event: {Enum.GetName(typeof(NotifyTableChangedAction), body.Action)}\r\n{JsonConvert.SerializeObject(obj)}");
            return body.Action switch
            {
                NotifyTableChangedAction.Insert => _db.Insert(obj) > 0 ? Ok() : StatusCode((int) HttpStatusCode.InternalServerError),
                NotifyTableChangedAction.Update => _db.Update(obj) > 0 ? Ok() : StatusCode((int) HttpStatusCode.InternalServerError),
                NotifyTableChangedAction.Delete => _db.Delete(obj) > 0 ? Ok() : StatusCode((int) HttpStatusCode.InternalServerError),
                _ => BadRequest()
            };
        }

        [HttpPost("cluster")]
        [ProducesResponseType((int) HttpStatusCode.OK)]
        [ProducesResponseType((int) HttpStatusCode.Unauthorized)]
        [ProducesResponseType((int) HttpStatusCode.Forbidden)]
        [Authorize(Roles = "UserKey")]
        public ActionResult AddToCluster([FromBody] AddToClusterRequest body)
        {
            _logger.LogInformation($"Adding to cluster; source server at {body.Hostname}:{body.Port}");

            var req = new RestRequest($"http://{body.Hostname}:{body.Port}/sync/getDb");

            req.AddHeader("X-Api-Key", body.ApiKey);

            try
            {
                _logger.LogInformation($"Downloading database from source server at {body.Hostname}:{body.Port}");

                var response = _client.Execute<SyncMessage>(req);

                if (!response.IsSuccessful)
                {
                    return StatusCode((int) HttpStatusCode.InternalServerError, $"Remote server returned an error: {response.GetErrorMessage()}");
                }

                var data = response.Data;

                if (data.MessageVersion > SyncMessage.GetLocalMessageVersion())
                {
                    return StatusCode((int) HttpStatusCode.InternalServerError, $"Remote server returned a newer message version; please update the local server and try again. ({data.MessageVersion} > {SyncMessage.GetLocalMessageVersion()})");
                }

                _db.Table<ApiKey>().Delete(_ => true);
                _db.InsertAll(data.ApiKeys);

                _db.Table<DnsDomain>().Delete(_ => true);
                _db.InsertAll(data.DnsDomains);

                _db.Table<DnsRecord>().Delete(_ => true);
                _db.InsertAll(data.DnsRecords);

                _db.Table<Node>().Delete(_ => true);
                _db.InsertAll(data.Nodes);

                var configuration = _db.Table<InternalConfiguration>().Single();

                var localNode = new Node
                {
                    Id = Guid.NewGuid(),
                    Hostname = configuration.Hostname,
                    Port = configuration.Port,
                    NodeId = configuration.NodeId,
                    LastChanged = DateTimeOffset.Now.ToUniversalTime()
                };

                _db.Insert(localNode);

                Synchronizer.Get().Send(localNode, NotifyTableChangedAction.Insert, _db, _logger);

                _logger.LogInformation("Cluster joined");

                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Could not add to cluster: {ex.Message}");

                return StatusCode((int) HttpStatusCode.InternalServerError, ex.Message);
            }
        }
    }
}