using Lodestone.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lodestone.Infrastructure.Data.Configurations;

public sealed class VolunteerProfileConfiguration : IEntityTypeConfiguration<VolunteerProfile>
{
    public void Configure(EntityTypeBuilder<VolunteerProfile> builder)
    {
        builder.Property(profile => profile.UserId).IsRequired().HasMaxLength(450);
        builder.Property(profile => profile.Department).HasMaxLength(200);
        builder.Property(profile => profile.Skills).HasMaxLength(500);
        builder.Property(profile => profile.Availability).HasMaxLength(500);
        builder.Property(profile => profile.Bio).HasMaxLength(2000);

        builder.HasIndex(profile => profile.UserId).IsUnique();
        builder.HasIndex(profile => new { profile.IsApproved, profile.IsActive });

        builder.HasOne(profile => profile.User)
            .WithOne(user => user.VolunteerProfile)
            .HasForeignKey<VolunteerProfile>(profile => profile.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
