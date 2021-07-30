using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using GoldsparkIT.DnsBackend.Models;
using GoldsparkIT.DnsBackend.Responses.DnsBackend;
using Microsoft.AspNetCore.Mvc;
using RestSharp;
using SQLite;

namespace GoldsparkIT.DnsBackend
{
    public static class Utility
    {
        public static string RandomString(int length, string allowedCharacters = @"abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_=+!@#$%^&*()[{]}\|;:'""/?.>,<")
        {
            return new(new string(' ', length).Select(x => allowedCharacters[RandomNumberGenerator.GetInt32(0, allowedCharacters.Length)]).ToArray());
        }

        public static bool HasPriority(string type)
        {
            return type.ToLower() switch
            {
                "cert" => true,
                "mx" => true,
                "naptr" => true,
                "srv" => true,
                _ => false
            };
        }

        public static string GetErrorMessage(this IRestResponse response)
        {
            return response.StatusCode == 0 ? response.ErrorMessage : $"{(int) response.StatusCode} {response.StatusDescription}\r\nContent: {response.Content}";
        }

        public static TValue Get<TValue>(this IDictionary<string, TValue> dictionary, string key)
        {
            return dictionary.SingleOrDefault(kvp => kvp.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;
        }

        public static TValue GetAs<TValue>(this IDictionary<string, object> dictionary, string key)
        {
            return (TValue) dictionary.Get(key);
        }

        public static ActionResult<IResponse> ReturnSoaRecord(string qname, SQLiteConnection db)
        {
            var soaRecord = GetSoaRecord(qname, db);

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

        public static ActionResult<IResponse> ReturnNsRecords(string qname, SQLiteConnection db)
        {
            var nsRecords = GetNsRecords(qname, db);

            if (nsRecords == null || !nsRecords.Any())
            {
                return new BaseResponse {result = false};
            }

            return new LookupResponse
            {
                result = nsRecords
            };
        }

        public static IEnumerable<LookupRecord> GetNsRecords(string qname, SQLiteConnection db)
        {
            var domain = db.Table<DnsDomain>().SingleOrDefault(x => x.Domain.Equals(qname, StringComparison.OrdinalIgnoreCase));

            if (domain == null)
            {
                return null;
            }

            var hosts = db.Table<Node>().Select(n => n.Hostname);

            if (!hosts.Any())
            {
                hosts = new List<string> {db.Table<InternalConfiguration>().Single().Hostname};
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

        public static LookupRecord GetSoaRecord(string qname, SQLiteConnection db)
        {
            var domain = db.Table<DnsDomain>().SingleOrDefault(x => x.Domain.Equals(qname, StringComparison.OrdinalIgnoreCase));

            if (domain == null)
            {
                return null;
            }

            var masterHost = db.Table<Node>().SingleOrDefault(n => n.NodeId.Equals(db.Table<Node>().Min(x => x.NodeId)))?.Hostname ?? db.Table<InternalConfiguration>().Single().Hostname;

            return new LookupRecord
            {
                qname = domain.Domain,
                qtype = "SOA",
                content = $"{masterHost}. {domain.RName}. {domain.Serial} {domain.Refresh} {domain.Retry} {domain.Expire} {domain.Ttl}",
                auth = 1,
                ttl = domain.Ttl
            };
        }

        public static Regex CreateDnsRegex(DnsRecord record)
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

            regex += $"{Regex.Escape($"{record.Domain}")}{Regex.Escape(".")}?$";

            return new Regex(regex, RegexOptions.IgnoreCase);
        }

        public static string CreateFqdn(DnsRecord record)
        {
            var result = "";

            if (!record.Name.Equals("@") && !string.IsNullOrWhiteSpace(record.Name))
            {
                result = $"{record.Name}.";
            }

            result += $"{record.Domain}";

            return result;
        }
    }
}