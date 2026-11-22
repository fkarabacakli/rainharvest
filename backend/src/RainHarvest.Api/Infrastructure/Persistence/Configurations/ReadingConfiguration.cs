using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RainHarvest.Api.Domain;

namespace RainHarvest.Api.Infrastructure.Persistence.Configurations;

internal sealed class ReadingConfiguration : IEntityTypeConfiguration<Reading>
{
    public void Configure(EntityTypeBuilder<Reading> builder)
    {
        // A hypertable's unique indexes must include the time column,
        // so the key is (device_id, received_at) and there is no surrogate id.
        builder.HasKey(r => new { r.DeviceId, r.ReceivedAt });
        builder.Property(r => r.DeviceId).HasMaxLength(Device.MaxIdLength);

        builder.HasOne<Device>()
            .WithMany()
            .HasForeignKey(r => r.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
