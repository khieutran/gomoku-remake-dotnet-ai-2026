using Gomoku.Application.Matches;
using Gomoku.Infrastructure.Matches;

namespace Gomoku.Api.Matches;

public static class GameFlowSetup
{
    public static void AddGomokuGameFlow(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IGameFlowStore, EfGameFlowStore>();
        builder.Services.AddScoped<GameFlowService>();
        builder.Services.AddScoped<IActiveMatchReader, EfActiveMatchReader>();
        builder.Services.AddScoped<ActiveMatchService>();
    }
}
