using System;
using System.IO;
using System.Linq;
using System.Net;
using GoldsparkIT.DnsBackend.Models;
using SQLite;

namespace GoldsparkIT.DnsBackend
{
    public static class DbProvider
    {
        public static SQLiteConnection ProvideSQLiteConnection(IServiceProvider provider)
        {
            return ProvideSQLiteConnection();
        }

        public static SQLiteConnection ProvideSQLiteConnection(bool readOnly = false)
        {
            var dbFolder = GetDbFolder();

            try
            {
                Directory.CreateDirectory(dbFolder);
            }
            catch
            {
                // Ignore
            }

            var dbFile = GetDbPath();

            return new SQLiteConnection(dbFile, (readOnly ? SQLiteOpenFlags.NoMutex | SQLiteOpenFlags.ReadOnly : SQLiteOpenFlags.Create | SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.FullMutex) | SQLiteOpenFlags.ProtectionComplete);
        }

        private static string GetDbPath()
        {
            return Path.Combine(GetDbFolder(), "dns.db");
        }

        private static string GetDbFolder()
        {
            var baseFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            if (string.IsNullOrWhiteSpace(baseFolder) || baseFolder == "/")
            {
                baseFolder = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            }

            if (string.IsNullOrWhiteSpace(baseFolder) || baseFolder == "/")
            {
                switch (Environment.OSVersion.Platform)
                {
                    case PlatformID.Win32NT:
                    case PlatformID.Win32S:
                    case PlatformID.Win32Windows:
                    case PlatformID.WinCE:
                    case PlatformID.Xbox:
                        baseFolder = $"{Environment.GetFolderPath(Environment.SpecialFolder.Windows)[..1]}:\\ProgramData\\";
                        break;
                    case PlatformID.Unix:
                        baseFolder = "/var/lib/";
                        break;
                    case PlatformID.MacOSX:
                        baseFolder = "/Library/Application Support/";
                        break;
                    case PlatformID.Other:
                        throw new Exception("Could not determine data path");
                }
            }

            var dbPath = Path.Combine(baseFolder, "GoldsparkIT.DnsBackend");
            return dbPath;
        }

        public static void InitializeDatabase()
        {
            using var db = ProvideSQLiteConnection();

            db.CreateTable<DnsDomain>();
            db.CreateTable<DnsRecord>();
            db.CreateTable<ApiKey>();
            db.CreateTable<Node>();
            db.CreateTable<InternalConfiguration>();

            if (!db.Table<InternalConfiguration>().Any())
            {
                db.Insert(new InternalConfiguration
                {
                    Id = Guid.NewGuid(),
                    Hostname = Dns.GetHostName(),
                    NodeId = Guid.NewGuid(),
                    Port = 5236,
                    ZeroMqPort = 5237
                });
            }

            if (!db.Table<ApiKey>().Any(k => !k.ClusterKey))
            {
                var key = Utility.RandomString(64);
                db.Insert(new ApiKey
                {
                    Id = Guid.NewGuid(),
                    Name = "Initial Key",
                    Key = key,
                    ClusterKey = false
                });

                Console.WriteLine("IMPORTANT: New API key generated!");
                Console.WriteLine(key);
                Console.WriteLine("Please store this key somewhere safe. It will only be shown once!");
            }

            if (!db.Table<ApiKey>().Any(k => k.ClusterKey))
            {
                db.Insert(new ApiKey
                {
                    Id = Guid.NewGuid(),
                    Name = "Cluster Key",
                    Key = Utility.RandomString(64),
                    ClusterKey = true
                });
            }

            var configuration = db.Table<InternalConfiguration>().Single();

            if (!db.Table<Node>().Any(n => n.NodeId == configuration.NodeId))
            {
                db.Insert(new Node
                {
                    Id = Guid.NewGuid(),
                    NodeId = configuration.NodeId,
                    Hostname = configuration.Hostname,
                    Port = configuration.Port
                });
            }
        }
    }
}