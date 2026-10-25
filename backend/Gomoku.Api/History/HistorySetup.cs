using Gomoku.Application.History;
using Gomoku.Infrastructure.History;

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
                options.InstanceName = "gomoku:";
            });
            builder.Services.AddScoped<IHistoryCache, RedisHistoryCache>();
        }
    }
}
