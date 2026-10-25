using Gomoku.Infrastructure.Redis;

namespace Gomoku.Api.Redis;

public static class RedisSetup
{
    public static void AddGomokuRedisKeyspace(this WebApplicationBuilder builder)
    {
        var configured = builder.Configuration["GOMOKU_REDIS_NAMESPACE"];
        var redisEnabled = !string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Redis"));
        if (redisEnabled && string.IsNullOrWhiteSpace(configured) && !builder.Environment.IsDevelopment())
            throw new InvalidOperationException(
                "GOMOKU_REDIS_NAMESPACE is required when Redis is enabled outside Development.");
        builder.Services.AddSingleton(new RedisKeyspace(
            string.IsNullOrWhiteSpace(configured) ? "gomoku:development" : configured));
    }
}
