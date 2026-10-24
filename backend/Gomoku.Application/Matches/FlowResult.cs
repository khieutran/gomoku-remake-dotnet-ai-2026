namespace Gomoku.Application.Matches;

public sealed record FlowResult(string Code, int? HttpStatus = null, string? Body = null,
    Guid? MatchId = null, string? CommandId = null, long? Revision = null)
{
    public static FlowResult Error(string code) => new(code);

    public static FlowResult Success(CommandRecord receipt) => new(
        "ok", receipt.HttpStatus, receipt.ResponseBody,
        receipt.MatchId, receipt.CommandId, receipt.Revision);
}
