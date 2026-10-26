using Gomoku.AI;
using Gomoku.Application.Matches;
using Gomoku.Infrastructure.Matches;

namespace Gomoku.Api.Matches;

public static class GameFlowSetup
{
    public static void AddGomokuGameFlow(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IGameFlowStore, EfGameFlowStore>();
        builder.Services.AddScoped<IAiTurnJobStore, EfAiTurnJobStore>();
        builder.Services.AddScoped<GameFlowService>();
        builder.Services.AddSingleton<IAiMoveSelector>(new SearchMoveSelector(2026));
        if (builder.Configuration.GetValue("AiWorker:Enabled", true))
            builder.Services.AddHostedService<AiTurnWorker>();
        builder.Services.AddScoped<IActiveMatchReader, EfActiveMatchReader>();
        builder.Services.AddScoped<ActiveMatchService>();
    }
}
