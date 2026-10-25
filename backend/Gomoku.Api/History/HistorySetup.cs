using Gomoku.Application.History;
using Gomoku.Infrastructure.History;
using Gomoku.Infrastructure.Redis;
using Microsoft.Extensions.Caching.StackExchangeRedis;

namespace Gomoku.Api.History;

public static class HistorySetup
{
    public static void AddGomokuHistory(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IHistoryReader, EfHistoryReader>();
        builder.Services.AddScoped<HistoryService>();
        var redis = builder.Configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(redis))
        {
            builder.Services.AddSingleton<IHistoryCache, NoopHistoryCache>();
        }
        else
        {
            builder.Services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redis;
            });
            builder.Services.AddOptions<RedisCacheOptions>()
                .Configure<RedisKeyspace>((options, keyspace) =>
                    options.InstanceName = $"{keyspace.Value}:");
            builder.Services.AddScoped<IHistoryCache, RedisHistoryCache>();
        }
    }
}
