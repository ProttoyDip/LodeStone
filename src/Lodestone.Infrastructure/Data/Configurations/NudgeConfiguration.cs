using Lodestone.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lodestone.Infrastructure.Data.Configurations;

public class NudgeConfiguration : IEntityTypeConfiguration<Nudge>
{
    public void Configure(EntityTypeBuilder<Nudge> builder)
    {
        // Existing installations may contain legacy prompts. Keep their storage type unchanged;
        // the Application service owns the bounded, template-only manual creation policy.
        builder.Property(nudge => nudge.Message).IsRequired();
        builder.HasIndex(nudge => new { nudge.StudentProfileId, nudge.Status, nudge.AvailableAtUtc });

        builder.HasOne(nudge => nudge.StudentProfile)
            .WithMany(profile => profile.Nudges)
            .HasForeignKey(nudge => nudge.StudentProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_Nudges_VisibilityWindow",
                "[ExpiresAtUtc] > [AvailableAtUtc]");
        });
    }
}
