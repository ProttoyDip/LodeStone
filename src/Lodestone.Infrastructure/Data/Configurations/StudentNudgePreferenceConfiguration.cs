using Lodestone.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lodestone.Infrastructure.Data.Configurations;

public sealed class StudentNudgePreferenceConfiguration : IEntityTypeConfiguration<StudentNudgePreference>
{
    public void Configure(EntityTypeBuilder<StudentNudgePreference> builder)
    {
        builder.Property(preference => preference.CreatedBy).HasMaxLength(450);
        builder.Property(preference => preference.ModifiedBy).HasMaxLength(450);
        builder.HasIndex(preference => preference.StudentProfileId).IsUnique();
        builder.HasOne(preference => preference.StudentProfile)
            .WithOne(profile => profile.NudgePreference)
            .HasForeignKey<StudentNudgePreference>(preference => preference.StudentProfileId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
