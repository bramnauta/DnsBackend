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
        public static void Main(string[] args)
        {
            var db = DbProvider.ProvideSQLiteConnection();

            var configuration = db.Table<InternalConfiguration>().Single();

            var port = configuration.Port;

            CreateHostBuilder(args, port).Build().Run();
        }

        private static IHostBuilder CreateHostBuilder(string[] args, int port)
        {
            return Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    var hostingEnvironment = hostingContext.HostingEnvironment;

                    config.Sources.Clear();
                    config.AddJsonFile("appsettings.json", true, false)
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
                    webBuilder.UseKestrel(options => options.ListenAnyIP(port));
                });
        }
    }
}