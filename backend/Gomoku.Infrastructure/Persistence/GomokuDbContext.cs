using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Gomoku.Infrastructure.Persistence;

public sealed class GomokuDbContext(DbContextOptions<GomokuDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<GuestSession> GuestSessions => Set<GuestSession>();
    public DbSet<StoredMatch> Matches => Set<StoredMatch>();
    public DbSet<StoredGame> Games => Set<StoredGame>();
    public DbSet<StoredMove> Moves => Set<StoredMove>();
    public DbSet<CommandReceipt> CommandReceipts => Set<CommandReceipt>();
    public DbSet<RankingOutbox> RankingOutbox => Set<RankingOutbox>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(user =>
        {
            user.Property(x => x.HistoryRevision).HasDefaultValue(0);
            user.Property(x => x.RankingPoints).HasDefaultValue(0);
            user.Property(x => x.RankingGames).HasDefaultValue(0);
            user.ToTable(table => table.HasCheckConstraint("CK_User_HistoryRevision", "\"HistoryRevision\" >= 0"));
            user.ToTable(table => table.HasCheckConstraint("CK_User_Ranking", "\"RankingPoints\" >= 0 AND \"RankingGames\" >= 0"));
        });

        builder.Entity<GuestSession>(guest =>
        {
            guest.HasKey(x => x.Id);
            guest.Property(x => x.TokenHash).IsRequired();
            guest.HasIndex(x => x.TokenHash).IsUnique();
            guest.Property(x => x.HistoryRevision).HasDefaultValue(0);
            guest.ToTable(table => table.HasCheckConstraint("CK_GuestSession_Revision", "\"HistoryRevision\" >= 0"));
        });

        builder.Entity<StoredMatch>(match =>
        {
            match.ToTable("Matches", table =>
            {
                table.HasCheckConstraint("CK_Match_OneOwner", "(\"UserId\" IS NULL) <> (\"GuestSessionId\" IS NULL)");
                table.HasCheckConstraint("CK_Match_Nonnegative", "\"XScore\" >= 0 AND \"OScore\" >= 0 AND \"Revision\" >= 0");
                table.HasCheckConstraint("CK_Match_BoardSize", "\"BoardSize\" > 0");
            });
            match.HasKey(x => x.Id);
            match.Property(x => x.RuleVersion).HasMaxLength(64).IsRequired();
            match.Property(x => x.UserId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            match.Property(x => x.GuestSessionId).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            match.Property(x => x.RuleVersion).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            match.Property(x => x.BoardSize).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            match.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            match.HasOne<GuestSession>().WithMany().HasForeignKey(x => x.GuestSessionId).OnDelete(DeleteBehavior.Restrict);
            match.HasOne<StoredGame>().WithMany()
                .HasForeignKey(x => new { x.CurrentGameId, x.Id })
                .HasPrincipalKey(x => new { x.Id, x.MatchId })
                .OnDelete(DeleteBehavior.Restrict);
            match.HasIndex(x => new { x.UserId, x.CreatedAt });
            match.HasIndex(x => new { x.GuestSessionId, x.CreatedAt });
        });

        builder.Entity<StoredGame>(game =>
        {
            game.ToTable("Games", table =>
            {
                table.HasCheckConstraint("CK_Game_Number", "\"Number\" > 0");
                table.HasCheckConstraint("CK_Game_OpeningStone", "\"OpeningStone\" IN ('X', 'O')");
            });
            game.HasKey(x => x.Id);
            game.HasOne<StoredMatch>().WithMany().HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Restrict);
            game.HasIndex(x => new { x.MatchId, x.Number }).IsUnique();
            game.HasIndex(x => x.MatchId).IsUnique().HasFilter("\"State\" = 'Active'");
            game.Property(x => x.OpeningStone).HasConversion<string>().HasMaxLength(1);
            game.Property(x => x.RuleStatus).HasConversion<string>().HasMaxLength(16);
            game.Property(x => x.State).HasConversion<string>().HasMaxLength(16);
            game.Property(x => x.AiVersion).HasMaxLength(128);
        });

        builder.Entity<StoredMove>(move =>
        {
            move.ToTable("Moves", table =>
            {
                table.HasCheckConstraint("CK_Move_Step", "\"Step\" > 0");
                table.HasCheckConstraint("CK_Move_Position", "\"Row\" >= 0 AND \"Column\" >= 0");
                table.HasCheckConstraint("CK_Move_Stone", "\"Stone\" IN ('X', 'O')");
            });
            move.HasKey(x => x.Id);
            move.HasOne<StoredGame>().WithMany().HasForeignKey(x => x.GameId).OnDelete(DeleteBehavior.Restrict);
            move.HasIndex(x => new { x.GameId, x.Step }).IsUnique();
            move.HasIndex(x => new { x.GameId, x.Row, x.Column }).IsUnique();
            move.Property(x => x.Stone).HasConversion<string>().HasMaxLength(1);
            move.Property(x => x.Actor).HasConversion<string>().HasMaxLength(8);
        });

        builder.Entity<CommandReceipt>(receipt =>
        {
            receipt.ToTable("CommandReceipts", table =>
            {
                table.HasCheckConstraint("CK_Receipt_OneOwner", "(\"UserId\" IS NULL) <> (\"GuestSessionId\" IS NULL)");
                table.HasCheckConstraint("CK_Receipt_Status", "\"HttpStatus\" BETWEEN 200 AND 299");
            });
            receipt.HasKey(x => x.Id);
            receipt.Property(x => x.CommandId).HasMaxLength(128).IsRequired();
            receipt.Property(x => x.RequestHash).HasMaxLength(128).IsRequired();
            receipt.Property(x => x.ResponseBody).IsRequired();
            receipt.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            receipt.HasOne<GuestSession>().WithMany().HasForeignKey(x => x.GuestSessionId).OnDelete(DeleteBehavior.Restrict);
            receipt.HasOne<StoredMatch>().WithMany().HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Restrict);
            receipt.HasIndex(x => new { x.UserId, x.CommandId }).IsUnique().HasFilter("\"UserId\" IS NOT NULL");
            receipt.HasIndex(x => new { x.GuestSessionId, x.CommandId }).IsUnique().HasFilter("\"GuestSessionId\" IS NOT NULL");
        });

        builder.Entity<RankingOutbox>(ranking =>
        {
            ranking.ToTable("RankingOutbox", table =>
                table.HasCheckConstraint("CK_RankingOutbox_Points", "\"Points\" >= 0"));
            ranking.HasKey(x => x.Id);
            ranking.HasIndex(x => x.GameId).IsUnique();
            ranking.HasIndex(x => new { x.PublishedAt, x.CreatedAt });
            ranking.HasOne<StoredGame>().WithMany().HasForeignKey(x => x.GameId)
                .OnDelete(DeleteBehavior.Restrict);
            ranking.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
