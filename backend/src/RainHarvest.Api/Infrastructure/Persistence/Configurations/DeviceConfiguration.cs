using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RainHarvest.Api.Domain;

namespace RainHarvest.Api.Infrastructure.Persistence.Configurations;

internal sealed class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasMaxLength(Device.MaxIdLength);
        builder.Property(d => d.Name).HasMaxLength(128);

        // Deleting a site leaves its devices unassigned rather than deleting their history.
        builder.HasOne<Site>().WithMany().HasForeignKey(d => d.SiteId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(d => d.SiteId);
    }
}
