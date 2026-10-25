namespace Gomoku.Application.Ranking;

public sealed record RankingEntry(Guid AccountId, int Points, int Rank);
public sealed record RankingPage(IReadOnlyList<RankingEntry> Items);
public sealed record RankingPosition(Guid AccountId, int Points, int? Rank);
public sealed record RankingScore(Guid AccountId, int Points);
