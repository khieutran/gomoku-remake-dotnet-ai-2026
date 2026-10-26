using Gomoku.AI;
using Gomoku.Application.Matches;
using Gomoku.Infrastructure.Matches;
using Gomoku.Infrastructure.AI;

namespace Gomoku.Api.Matches;

public static class GameFlowSetup
{
    public static void AddGomokuGameFlow(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IGameFlowStore, EfGameFlowStore>();
        builder.Services.AddScoped<IAiTurnJobStore, EfAiTurnJobStore>();
        builder.Services.AddScoped<GameFlowService>();
        builder.Services.AddSingleton(new SearchMoveSelector(2026));
        builder.Services.AddSingleton<IAiMoveSelector>(services =>
            services.GetRequiredService<SearchMoveSelector>());
        var pointer = new ChampionPointerAiRegistry(
            builder.Configuration["AiModels:ChampionPointerPath"]);
        builder.Services.AddSingleton(pointer);
        builder.Services.AddSingleton<IAiVersionProvider>(pointer);
        builder.Services.AddSingleton<IPolicyModelRegistry>(pointer);
        builder.Services.AddSingleton<ModelGuidedMoveSelector>();
        if (builder.Configuration.GetValue("AiWorker:Enabled", true))
            builder.Services.AddHostedService<AiTurnWorker>();
        builder.Services.AddScoped<IActiveMatchReader, EfActiveMatchReader>();
        builder.Services.AddScoped<ActiveMatchService>();
    }
}
