using Gomoku.Application.Matches;

namespace Gomoku.Application.Datasets;

public enum DatasetSplit
{
    Train,
    Validation,
    Test
}

public sealed record DatasetExportSettings(string Seed, DateTimeOffset ExportCutoffUtc,
    DateTimeOffset TestCutoffUtc);

public sealed record GuestMatchSource(MatchRecord Match,
    IReadOnlyDictionary<Guid, DateTimeOffset?> FinishedAtByGame, DateTimeOffset CreatedAt);

public sealed record DatasetMove(int Step, int Row, int Column, string Stone, string Actor,
    string? ActualAiVersion, string? AiFallbackReason);

public sealed record DatasetGameRow(string Source, string Split, string RuleVersion,
    string AiVersion, string OpeningStone, string Result, string ContentHash,
    IReadOnlyList<DatasetMove> Moves);
