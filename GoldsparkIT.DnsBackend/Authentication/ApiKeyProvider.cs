using System.Linq;
using System.Threading.Tasks;
using GoldsparkIT.DnsBackend.Models;
using SQLite;

namespace GoldsparkIT.DnsBackend.Authentication
{
    public class ApiKeyProvider : IApiKeyProvider
    {
        private readonly SQLiteConnection _db;

        public ApiKeyProvider(SQLiteConnection db)
        {
            _db = db;
        }

        public Task<ApiKey> Execute(string providedApiKey)
        {
            return Task.FromResult(_db.Table<ApiKey>().SingleOrDefault(r => r.Key.Equals(providedApiKey)));
        }
    }
}