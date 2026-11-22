using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RainHarvest.Api.Domain;

namespace RainHarvest.Api.Infrastructure.Persistence.Configurations;

internal sealed class DeviceSettingsConfiguration : IEntityTypeConfiguration<DeviceSettings>
{
    public void Configure(EntityTypeBuilder<DeviceSettings> builder)
    {
        builder.HasKey(s => s.DeviceId);
        builder.Property(s => s.DeviceId).HasMaxLength(Device.MaxIdLength);
        builder.HasOne<Device>().WithOne().HasForeignKey<DeviceSettings>(s => s.DeviceId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DeviceCommandConfiguration : IEntityTypeConfiguration<DeviceCommand>
{
    public void Configure(EntityTypeBuilder<DeviceCommand> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.DeviceId).HasMaxLength(Device.MaxIdLength);
        builder.Property(c => c.Action).HasMaxLength(32);
        builder.Property(c => c.Source).HasConversion<string>().HasMaxLength(16);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(c => c.Reason).HasMaxLength(256);
        builder.Property(c => c.StatusReason).HasMaxLength(64);

        // History per device, newest first; and the rule's "last automatic irrigation" lookup.
        builder.HasIndex(c => new { c.DeviceId, c.CreatedAt });

        // The timeout sweeper only ever looks at commands still waiting for an answer.
        builder.HasIndex(c => c.CreatedAt).HasFilter("status = 'Sent'");

        builder.HasOne<Device>().WithMany().HasForeignKey(c => c.DeviceId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AlertConfiguration : IEntityTypeConfiguration<Alert>
{
    public void Configure(EntityTypeBuilder<Alert> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.DeviceId).HasMaxLength(Device.MaxIdLength);
        builder.Property(a => a.Type).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.Severity).HasConversion<string>().HasMaxLength(16);
        builder.Property(a => a.Message).HasMaxLength(256);

        // One open alert per device and type: repeated bad readings cannot pile up.
        builder.HasIndex(a => new { a.DeviceId, a.Type }).IsUnique().HasFilter("resolved_at IS NULL");
        builder.HasIndex(a => a.RaisedAt);

        builder.HasOne<Device>().WithMany().HasForeignKey(a => a.DeviceId).OnDelete(DeleteBehavior.Cascade);
    }
}
