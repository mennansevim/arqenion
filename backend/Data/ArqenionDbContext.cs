using Microsoft.EntityFrameworkCore;
using Arqenion.Api.Models;

namespace Arqenion.Api.Data;

public sealed class ArqenionDbContext : DbContext
{
    public ArqenionDbContext(DbContextOptions<ArqenionDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<SessionRecord> Sessions => Set<SessionRecord>();
    public DbSet<SessionEventRecord> SessionEvents => Set<SessionEventRecord>();
    public DbSet<AiFeedbackRecord> AiFeedbacks => Set<AiFeedbackRecord>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<SessionEventRecord>().HasIndex(e => new { e.SessionId, e.EventId }).IsUnique();
        b.Entity<SessionEventRecord>().HasIndex(e => e.SessionId);
        b.Entity<AiFeedbackRecord>().HasIndex(e => e.SessionId);
    }
}
