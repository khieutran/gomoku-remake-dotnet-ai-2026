using Gomoku.Application.History;
using Gomoku.Infrastructure.History;

namespace Gomoku.Api.History;

public static class HistorySetup
{
    public static void AddGomokuHistory(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IHistoryReader, EfHistoryReader>();
        builder.Services.AddScoped<HistoryService>();
    }
}
