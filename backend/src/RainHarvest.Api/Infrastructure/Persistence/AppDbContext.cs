using Microsoft.EntityFrameworkCore;
using RainHarvest.Api.Domain;

namespace RainHarvest.Api.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Device> Devices => Set<Device>();

    public DbSet<Reading> Readings => Set<Reading>();

    public DbSet<User> Users => Set<User>();

    public DbSet<Site> Sites => Set<Site>();

    public DbSet<SiteMember> SiteMembers => Set<SiteMember>();

    public DbSet<DeviceSettings> DeviceSettings => Set<DeviceSettings>();

    public DbSet<DeviceCommand> DeviceCommands => Set<DeviceCommand>();

    public DbSet<Alert> Alerts => Set<Alert>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Creates the extension in the first migration. Hypertables, continuous
        // aggregates and policies are added with raw SQL in migrations (EF does not model them).
        modelBuilder.HasPostgresExtension("timescaledb");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
