using Lodestone.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lodestone.Infrastructure.Data.Configurations;

public class CounselorBookingConfiguration : IEntityTypeConfiguration<CounselorBooking>
{
    public void Configure(EntityTypeBuilder<CounselorBooking> builder)
    {
        builder.HasIndex(item => new { item.StudentProfileId, item.ScheduledForUtc });
        builder.HasIndex(item => new { item.CounselorProfileId, item.ScheduledForUtc });
    }
}
