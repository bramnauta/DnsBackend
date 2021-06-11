using System.Linq;
using GoldsparkIT.DnsBackend.Models;
using Microsoft.AspNetCore.Hosting;
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
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                    webBuilder.UseKestrel(options => options.ListenAnyIP(port));
                });
        }
    }
}