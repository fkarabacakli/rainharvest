using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RainHarvest.Api.Domain;

namespace RainHarvest.Api.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Username).HasMaxLength(64);
        builder.HasIndex(u => u.Username).IsUnique();
        builder.Property(u => u.PasswordHash).HasMaxLength(512);
        builder.Property(u => u.Role).HasConversion<string>().HasMaxLength(16);
    }
}

internal sealed class SiteConfiguration : IEntityTypeConfiguration<Site>
{
    public void Configure(EntityTypeBuilder<Site> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).HasMaxLength(Site.MaxNameLength);
    }
}

internal sealed class SiteMemberConfiguration : IEntityTypeConfiguration<SiteMember>
{
    public void Configure(EntityTypeBuilder<SiteMember> builder)
    {
        builder.HasKey(m => new { m.SiteId, m.UserId });

        // Looking up "which sites does this user belong to" happens on every request.
        builder.HasIndex(m => m.UserId);

        builder.HasOne<Site>().WithMany().HasForeignKey(m => m.SiteId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
