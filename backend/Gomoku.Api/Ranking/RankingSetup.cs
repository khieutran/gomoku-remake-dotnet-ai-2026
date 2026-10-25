using Gomoku.Application.Ranking;
using Gomoku.Infrastructure.Ranking;
using StackExchange.Redis;

namespace Gomoku.Api.Ranking;

public static class RankingSetup
{
    public static void AddGomokuRanking(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IRankingReader, EfRankingReader>();
        builder.Services.AddScoped<RankingService>();
        var redis = builder.Configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(redis))
        {
            builder.Services.AddSingleton<IRankingProjection, NoopRankingProjection>();
        }
        else
        {
            builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
            {
                var options = ConfigurationOptions.Parse(redis);
                options.AbortOnConnectFail = false;
                options.ConnectTimeout = 500;
                options.AsyncTimeout = 500;
                return ConnectionMultiplexer.Connect(options);
            });
            builder.Services.AddSingleton<IRankingProjection, RedisRankingProjection>();
            builder.Services.AddHostedService<RankingProjectionWorker>();
        }
    }
}
