using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using GoldsparkIT.DnsBackend.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace GoldsparkIT.DnsBackend
{
    public class Program
    {
        private static ZeroMqResponder _zeroMqResponder;

        public static void Main(string[] args)
        {
            DbProvider.InitializeDatabase();

            int port;

            using (var db = DbProvider.ProvideSQLiteConnection())
            {
                var configuration = db.Table<InternalConfiguration>().Single();
                port = configuration.Port;

                if (configuration.ZeroMqPort > 0)
                {
                    Console.WriteLine($"Starting ZeroMQ server on port {configuration.ZeroMqPort}");
                    _zeroMqResponder = new ZeroMqResponder();
                    _zeroMqResponder.Start();
                }
            }

            CreateHostBuilder(args, port).Build().Run();
        }

        private static IHostBuilder CreateHostBuilder(string[] args, int port)
        {
            return Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    var hostingEnvironment = hostingContext.HostingEnvironment;

                    config.Sources.Clear();

                    // Try to find application folder
                    string baseDir = null;

                    try
                    {
                        baseDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
                    }
                    catch
                    {
                        // Ignore
                    }

                    if (string.IsNullOrWhiteSpace(baseDir))
                    {
                        try
                        {
                            baseDir = AppContext.BaseDirectory;
                        }
                        catch
                        {
                            // Ignore
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(baseDir))
                    {
                        config.SetBasePath(baseDir);
                    }

                    config.AddJsonFile("appsettings.json", false, false)
                        .AddJsonFile("appsettings." + hostingEnvironment.EnvironmentName + ".json", true, false);

                    if (!hostingEnvironment.IsDevelopment() || string.IsNullOrEmpty(hostingEnvironment.ApplicationName))
                    {
                        return;
                    }

                    var assembly = Assembly.Load(new AssemblyName(hostingEnvironment.ApplicationName));

                    if (assembly != null)
                    {
                        config.AddUserSecrets(assembly, true);
                    }
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                    webBuilder.UseKestrel(options =>
                    {
                        options.ListenAnyIP(port);
                        options.Limits.KeepAliveTimeout = TimeSpan.FromDays(365.25 * 2000);
                        options.Limits.MaxConcurrentConnections = long.MaxValue;
                        options.Limits.MaxConcurrentUpgradedConnections = long.MaxValue;
                        options.Limits.MaxResponseBufferSize = null;
                        options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
                        options.AddServerHeader = false;
                    });
                });
        }
    }
}