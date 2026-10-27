using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Gomoku.Application.Identity;
using Gomoku.Application.Matches;
using Gomoku.Core;

namespace Gomoku.Application.Datasets;

public sealed class GuestDatasetBuilder
{
    private readonly DatasetExportSettings _settings;
    private readonly HashSet<string> _seenGames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _rejections = new(StringComparer.Ordinal);

    public GuestDatasetBuilder(DatasetExportSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (string.IsNullOrWhiteSpace(settings.Seed))
            throw new ArgumentException("A stable split seed is required.", nameof(settings));
        if (settings.TestCutoffUtc > settings.ExportCutoffUtc)
            throw new ArgumentException("Test cutoff cannot be after export cutoff.", nameof(settings));
        _settings = settings;
    }

    public IReadOnlyDictionary<string, int> Rejections => _rejections;

    public IReadOnlyList<DatasetGameRow> Process(GuestMatchSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var match = source.Match;
        if (match.Owner.Scope != OwnerScope.Guest)
        {
            Reject("account_owner");
            return [];
        }
        try { MatchReplayer.Replay(match); }
        catch (StateIntegrityException)
        {
            Reject("invalid_replay");
            return [];
        }
        catch (UnsupportedRuleVersionException)
        {
            Reject("unsupported_rule_version");
            return [];
        }

        var split = source.CreatedAt > _settings.TestCutoffUtc
            ? SplitAfterTestFor(match.Id, _settings.Seed)
            : SplitFor(match.Id, _settings.Seed);
        var rows = new List<DatasetGameRow>();
        foreach (var game in match.Games.OrderBy(game => game.Number))
        {
            if (game.State != GameRecordState.Finished)
            {
                Reject("unfinished_game");
                continue;
            }
            if (!source.FinishedAtByGame.TryGetValue(game.Id, out var finishedAt) ||
                finishedAt is null || finishedAt > _settings.ExportCutoffUtc)
            {
                Reject("outside_export_cutoff");
                continue;
            }
            if (split == DatasetSplit.Test && finishedAt > _settings.TestCutoffUtc)
            {
                Reject("frozen_test_cutoff");
                continue;
            }
            if (string.IsNullOrWhiteSpace(game.AiVersion))
            {
                Reject("missing_ai_version");
                continue;
            }
            if (game.Moves.Any(move => move.Actor == PlayerActor.AI &&
                string.IsNullOrWhiteSpace(move.ActualAiVersion)))
            {
                Reject("missing_ai_move_provenance");
                continue;
            }
            if (game.Moves.Any(move => move.Actor == PlayerActor.AI &&
                (move.ActualAiVersion != game.AiVersion) !=
                !string.IsNullOrWhiteSpace(move.AiFallbackReason)))
            {
                Reject("inconsistent_ai_provenance");
                continue;
            }

            var hash = GameHash(match.RuleVersion, game);
            if (!_seenGames.Add(hash))
            {
                Reject("duplicate_game");
                continue;
            }
            rows.Add(new DatasetGameRow("guest", SplitName(split), match.RuleVersion,
                game.AiVersion, game.OpeningStone.ToString(), game.RuleStatus.ToString(), hash,
                game.Moves.Select(move => new DatasetMove(move.Step, move.Row, move.Column,
                    move.Stone.ToString(), move.Actor.ToString(), move.ActualAiVersion,
                    move.AiFallbackReason)).ToArray()));
        }
        return rows;
    }

    public static DatasetSplit SplitFor(Guid matchId, string seed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(seed);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{seed}:{matchId:N}"));
        var bucket = BinaryPrimitives.ReadUInt32BigEndian(hash) % 100;
        return bucket < 10 ? DatasetSplit.Test :
            bucket < 20 ? DatasetSplit.Validation : DatasetSplit.Train;
    }

    private static DatasetSplit SplitAfterTestFor(Guid matchId, string seed)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{seed}:post-test:{matchId:N}"));
        return BinaryPrimitives.ReadUInt32BigEndian(hash) % 10 == 0
            ? DatasetSplit.Validation : DatasetSplit.Train;
    }

    private static string SplitName(DatasetSplit split) => split switch
    {
        DatasetSplit.Train => "train",
        DatasetSplit.Validation => "validation",
        DatasetSplit.Test => "test",
        _ => throw new ArgumentOutOfRangeException(nameof(split))
    };

    private static string GameHash(string ruleVersion, GameRecord game)
    {
        var canonical = new StringBuilder(ruleVersion).Append('|').Append(game.OpeningStone)
            .Append('|').Append(game.RuleStatus);
        foreach (var move in game.Moves)
            canonical.Append('|').Append(move.Step).Append(':').Append(move.Row)
                .Append(':').Append(move.Column).Append(':').Append(move.Stone);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
    }

    private void Reject(string reason) => _rejections[reason] =
        _rejections.GetValueOrDefault(reason) + 1;
}
