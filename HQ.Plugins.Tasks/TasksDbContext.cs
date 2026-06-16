using HQ.Plugins.Tasks.Entities;
using Microsoft.EntityFrameworkCore;

namespace HQ.Plugins.Tasks;

public class TasksDbContext : DbContext
{
    public const string Schema = "plugin_localtasks";

    public DbSet<Project> Projects { get; set; }
    public DbSet<TaskItem> Tasks { get; set; }
    public DbSet<Comment> Comments { get; set; }

    public TasksDbContext(DbContextOptions<TasksDbContext> options)
        : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Project>()
            .HasIndex(p => new { p.OrganizationId, p.Name });

        modelBuilder.Entity<TaskItem>()
            .HasIndex(t => new { t.OrganizationId, t.ProjectId, t.Status });

        // Agent-scoped (project-less) task lookups.
        modelBuilder.Entity<TaskItem>()
            .HasIndex(t => new { t.OrganizationId, t.AgentId, t.Status });

        modelBuilder.Entity<Comment>()
            .HasIndex(c => c.TaskId);
    }
}
