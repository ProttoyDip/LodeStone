using Lodestone.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lodestone.Infrastructure.Data.Configurations;

public class StudentProfileConfiguration : IEntityTypeConfiguration<StudentProfile>
{
    public void Configure(EntityTypeBuilder<StudentProfile> builder)
    {
        builder.Property(profile => profile.UserId).IsRequired().HasMaxLength(450);
        builder.Property(profile => profile.StudentNumber).HasMaxLength(64);
        builder.Property(profile => profile.Program).HasMaxLength(200);

        builder.HasIndex(profile => profile.UserId).IsUnique();
        builder.HasIndex(profile => profile.StudentNumber)
            .HasDatabaseName("UX_StudentProfiles_StudentNumber")
            .HasFilter("[StudentNumber] IS NOT NULL")
            .IsUnique();

        builder.HasOne(profile => profile.User)
            .WithOne(user => user.StudentProfile)
            .HasForeignKey<StudentProfile>(profile => profile.UserId);
    }
}
