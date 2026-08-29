using Lodestone.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lodestone.Infrastructure.Data.Configurations;

public sealed class CounselorAvailabilitySlotConfiguration : IEntityTypeConfiguration<CounselorAvailabilitySlot>
{
    public void Configure(EntityTypeBuilder<CounselorAvailabilitySlot> builder)
    {
        builder.Property(item => item.RowVersion).IsRowVersion();
        builder.HasIndex(item => new { item.CounselorProfileId, item.StartUtc });
    }
}
