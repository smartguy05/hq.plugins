using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HQ.Plugins.Tasks;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TasksDbContext>
{
    public TasksDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TasksDbContext>()
            .UseNpgsql("Host=localhost;Database=hq;Username=postgres;Password=postgres")
            .Options;
        return new TasksDbContext(options);
    }
}
