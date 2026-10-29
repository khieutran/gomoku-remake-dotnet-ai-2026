using Gomoku.Core;

namespace Gomoku.AI;

public enum SearchDecisionReason
{
    NoMove,
    ImmediateWin,
    ImmediateBlock,
    ThreatBlock,
    ThreatAttack,
    Search,
    Fallback
}

public sealed record SearchResult(Position? Move, SearchDecisionReason Reason,
    int CompletedDepth, long Nodes, bool BudgetExpired);
