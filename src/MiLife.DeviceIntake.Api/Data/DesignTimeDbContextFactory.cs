using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MiLife.DeviceIntake.Api.Data;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<IntakeDbContext>
{
    public IntakeDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<IntakeDbContext>();
        options.UseSqlite("Data Source=device-intake-design.db");
        return new IntakeDbContext(options.Options);
    }
}
