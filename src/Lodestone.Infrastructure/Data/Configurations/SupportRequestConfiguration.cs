using Lodestone.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lodestone.Infrastructure.Data.Configurations;

public sealed class SupportRequestConfiguration : IEntityTypeConfiguration<SupportRequest>
{
    public void Configure(EntityTypeBuilder<SupportRequest> builder)
    {
        builder.Property(request => request.Title).IsRequired().HasMaxLength(200);
        builder.Property(request => request.Message).IsRequired().HasMaxLength(2000);
        builder.Property(request => request.Availability).HasMaxLength(500);
        builder.Property(request => request.RowVersion).IsRowVersion();

        builder.HasIndex(request => new { request.StudentProfileId, request.CreatedAtUtc });
        builder.HasIndex(request => new { request.VolunteerProfileId, request.Status, request.CreatedAtUtc });
        builder.HasIndex(request => new { request.Status, request.IsVisibleToVolunteers, request.CreatedAtUtc });

        builder.HasOne(request => request.StudentProfile)
            .WithMany(profile => profile.SupportRequests)
            .HasForeignKey(request => request.StudentProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(request => request.VolunteerProfile)
            .WithMany(profile => profile.SupportRequests)
            .HasForeignKey(request => request.VolunteerProfileId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
