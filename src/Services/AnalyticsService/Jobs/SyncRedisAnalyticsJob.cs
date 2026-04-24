using AnalyticsService.Data;
using AnalyticsService.Models;
using Quartz;
using StackExchange.Redis;

namespace AnalyticsService.Jobs
{
    public class SyncRedisAnalyticsJob : IJob
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly AppAnalyticsDbContext _dbContext;

        public SyncRedisAnalyticsJob(IConnectionMultiplexer redis, AppAnalyticsDbContext dbContext)
        {
            _redis = redis;
            _dbContext = dbContext;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            var db = _redis.GetDatabase();
            var redisKeys = _redis.GetServer("localhost", 6379).Keys(pattern: "analytics:*").ToList();
            
            if (!redisKeys.Any()) return;

            var analyticsList = new List<ClickRecord>();

            foreach (var key in redisKeys)
            {
                var userId = key.ToString().Split(':')[1];
                var entries = await db.ListRangeAsync(key);

                foreach (var entry in entries)
                {
                    var data = entry.ToString().Split('|');
                    analyticsList.Add(new ClickRecord
                    {
                        UserId = userId,
                        ShortCode = data[0],
                        CreatedDate = DateTime.Parse(data[1])
                    });
                }

                await db.KeyDeleteAsync(key); // Remove synced data from Redis
            }

            if (analyticsList.Any())
            {
                await _dbContext.ClickRecords.AddRangeAsync(analyticsList);
                await _dbContext.SaveChangesAsync();

                Console.WriteLine($"[Redis Analytics Sync] {analyticsList.Count} records synced to SQL DB.");
            }
        }
    }
}
