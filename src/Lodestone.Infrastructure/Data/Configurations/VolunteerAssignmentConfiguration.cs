using Lodestone.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lodestone.Infrastructure.Data.Configurations;

public sealed class VolunteerAssignmentConfiguration : IEntityTypeConfiguration<VolunteerAssignment>
{
    public void Configure(EntityTypeBuilder<VolunteerAssignment> builder)
    {
        builder.Property(assignment => assignment.Role).IsRequired().HasMaxLength(100);
        builder.Property(assignment => assignment.GroupName).HasMaxLength(200);
        builder.Property(assignment => assignment.Notes).HasMaxLength(500);

        builder.HasIndex(assignment => new { assignment.VolunteerProfileId, assignment.StudentProfileId })
            .IsUnique();
        builder.HasIndex(assignment => new { assignment.VolunteerProfileId, assignment.IsActive });
        builder.HasIndex(assignment => new { assignment.StudentProfileId, assignment.IsActive });

        builder.HasOne(assignment => assignment.VolunteerProfile)
            .WithMany(profile => profile.VolunteerAssignments)
            .HasForeignKey(assignment => assignment.VolunteerProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(assignment => assignment.StudentProfile)
            .WithMany(profile => profile.VolunteerAssignments)
            .HasForeignKey(assignment => assignment.StudentProfileId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
