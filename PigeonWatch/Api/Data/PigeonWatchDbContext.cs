using Microsoft.EntityFrameworkCore;
using PigeonWatch.Data.Conventions;
using PigeonWatch.Data.Entities;

namespace PigeonWatch.Data;

public class PigeonWatchDbContext(DbContextOptions<PigeonWatchDbContext> options) : DbContext(options)
{
    internal DbSet<UserAccountEntity> UserAccounts => Set<UserAccountEntity>();

    internal DbSet<SightingEntity> Sightings => Set<SightingEntity>();

    internal DbSet<SightingCommentEntity> SightingComments => Set<SightingCommentEntity>();

    internal DbSet<SightingThreadEntity> SightingThreads => Set<SightingThreadEntity>();

    internal DbSet<SightingMessageEntity> SightingMessages => Set<SightingMessageEntity>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Conventions.Add(_ => new UpperSnakeCaseNamingConvention());
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserAccountEntity>(entity =>
        {
            entity.ToTable("UserAccount");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();
            entity.HasIndex(e => e.NormalizedEmail).IsUnique();
            entity.HasIndex(e => e.NormalizedUserName).IsUnique();
            entity.HasIndex(e => e.NormalizedDisplayName).IsUnique();
        });

        modelBuilder.Entity<SightingEntity>(entity =>
        {
            entity.ToTable("Sighting");
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Reporter)
                .WithMany()
                .HasForeignKey(e => e.ReporterId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.Latitude, e.Longitude });
            entity.HasIndex(e => new { e.ReporterId, e.CreateDate });
        });

        modelBuilder.Entity<SightingCommentEntity>(entity =>
        {
            entity.ToTable("SightingComment");
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Sighting)
                .WithMany()
                .HasForeignKey(e => e.SightingId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Author)
                .WithMany()
                .HasForeignKey(e => e.AuthorId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.SightingId, e.CreateDate });
            entity.HasIndex(e => new { e.AuthorId, e.CreateDate });
        });

        modelBuilder.Entity<SightingThreadEntity>(entity =>
        {
            entity.ToTable("SightingThread");
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Sighting)
                .WithMany()
                .HasForeignKey(e => e.SightingId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Participant)
                .WithMany()
                .HasForeignKey(e => e.ParticipantId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.SightingId, e.ParticipantId }).IsUnique();
            entity.HasIndex(e => new { e.ParticipantId, e.LastMessageAt });
        });

        modelBuilder.Entity<SightingMessageEntity>(entity =>
        {
            entity.ToTable("SightingMessage");
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Thread)
                .WithMany()
                .HasForeignKey(e => e.ThreadId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Sender)
                .WithMany()
                .HasForeignKey(e => e.SenderId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.ThreadId, e.CreateDate });
        });
    }
}
