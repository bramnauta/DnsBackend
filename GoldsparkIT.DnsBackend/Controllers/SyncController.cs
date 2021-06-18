using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using GoldsparkIT.DnsBackend.Models;
using GoldsparkIT.DnsBackend.Requests;
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

        [HttpGet("download")]
        [ProducesResponseType(typeof(byte[]), (int) HttpStatusCode.OK)]
        [ProducesResponseType((int) HttpStatusCode.Unauthorized)]
        [ProducesResponseType((int) HttpStatusCode.Forbidden)]
        [Authorize(Roles = "UserKey")]
        public ActionResult Download()
        {
            var remoteAddr = Request?.HttpContext.Connection.RemoteIpAddress;

            _logger.LogInformation($"Received database download request from {remoteAddr}");

            DbProvider.Stop();

            var backupFile = Path.GetTempFileName();
            System.IO.File.Copy(DbProvider.GetDbPath(), backupFile, true);

            DbProvider.Start();

            using (var tempConn = new SQLiteConnection(backupFile, SQLiteOpenFlags.FullMutex | SQLiteOpenFlags.ReadWrite))
            {
                tempConn.DeleteAll<InternalConfiguration>();

                tempConn.Close();
            }

            var stream = new MemoryStream();

            using (var fileStream = new FileStream(backupFile, FileMode.Open, FileAccess.Read))
            {
                fileStream.CopyTo(stream);
                stream.Flush();
            }

            System.IO.File.Delete(backupFile);

            stream.Seek(0, SeekOrigin.Begin);

            return File(stream, "application/octet-stream", "dns.db", false);
        }

        [HttpGet("nodeId")]
        [ProducesResponseType(typeof(Guid), (int) HttpStatusCode.OK)]
        [ProducesResponseType((int) HttpStatusCode.Unauthorized)]
        [ProducesResponseType((int) HttpStatusCode.Forbidden)]
        [Authorize]
        public ActionResult GetNodeId()
        {
            return Ok(_db.Table<InternalConfiguration>().Single().NodeId);
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

            var path = DbProvider.GetDbPath();

            var req = new RestRequest($"http://{body.Hostname}:{body.Port}/sync/download");

            req.AddHeader("X-Api-Key", body.ApiKey);

            byte[] data;

            try
            {
                _logger.LogInformation($"Downloading database from source server at {body.Hostname}:{body.Port}");
                data = _client.DownloadData(req);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Could not add to cluster: {ex.Message}");
                return StatusCode((int) HttpStatusCode.InternalServerError, ex.Message);
            }

            if (data.LongLength <= 0)
            {
                _logger.LogError("Could not add to cluster: No data was retrieved from source server");
                return StatusCode((int) HttpStatusCode.InternalServerError, "No data was retrieved from source server");
            }

            _logger.LogInformation("Blocking all SQLite connections in preparation of joining cluster");
            DbProvider.Stop();

            try
            {
                _db.Close();
                _db.Dispose();
            }
            catch
            {
                // Ignore
            }

            for (var i = 0; i < 100; ++i)
            {
                try
                {
                    System.IO.File.Move(path, $"{path}.bck", true);
                    break;
                }
                catch
                {
                    Thread.Sleep(100);
                }
            }

            if (System.IO.File.Exists(path))
            {
                _logger.LogError("Could not add to cluster: Could not remove database");
                return StatusCode((int) HttpStatusCode.InternalServerError);
            }

            try
            {
                System.IO.File.WriteAllBytes(path, data);

                _logger.LogInformation("Releasing SQLite database block");
                DbProvider.Start();

                try
                {
                    System.IO.File.Delete($"{path}.bck");
                }
                catch
                {
                    // Ignore
                }

                var db = DbProvider.ProvideSQLiteConnection();
                var configuration = db.Table<InternalConfiguration>().Single();

                var localNode = new Node
                {
                    Id = Guid.NewGuid(),
                    Hostname = configuration.Hostname,
                    Port = configuration.Port,
                    NodeId = configuration.NodeId,
                    LastChanged = DateTimeOffset.Now.ToUniversalTime()
                };

                db.Insert(localNode);

                Synchronizer.Get().Send(localNode, NotifyTableChangedAction.Insert, _db);

                _logger.LogInformation("Cluster joined");

                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Could not add to cluster: {ex.Message}");

                System.IO.File.Move($"{path}.bck", path, true);

                return StatusCode((int) HttpStatusCode.InternalServerError, ex.Message);
            }
        }
    }
}