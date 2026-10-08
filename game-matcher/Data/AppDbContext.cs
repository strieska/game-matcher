namespace GameMatcher.Data
{
    using GameMatcher.Models;
    using Microsoft.EntityFrameworkCore;

    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options) {}

        public DbSet<Player> Players { get; set; }
        public DbSet<Event> Events { get; set; }
        public DbSet<Attendance> Attendances { get; set; }
        public DbSet<TeamAssignment> TeamAssignments { get; set; }
        public DbSet<MatchResult> MatchResults { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Attendance>().HasKey(x => new { x.EventId, x.PlayerId });
            modelBuilder.Entity<MatchResult>().HasKey(x => x.EventId);
            modelBuilder.Entity<Event>().HasMany(x => x.Attendances).WithOne(x => x.Event)
                .HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<Event>().HasMany(x => x.Assignments).WithOne(x => x.Event)
                .HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<Player>().Property(x => x.Elo).HasDefaultValue(1500);
        }
    }
}