using Lodestone.Domain.Entities;
using Lodestone.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lodestone.Infrastructure.Data.Configurations;

public sealed class StudentNumberClaimConfiguration : IEntityTypeConfiguration<StudentNumberClaim>
{
    public void Configure(EntityTypeBuilder<StudentNumberClaim> builder)
    {
        builder.Property(claim => claim.ClaimedStudentNumber).IsRequired().HasMaxLength(64);
        builder.Property(claim => claim.ReviewedByUserId).HasMaxLength(450);
        builder.Property(claim => claim.RowVersion).IsRowVersion().IsConcurrencyToken();

        builder.HasIndex(claim => claim.StudentProfileId)
            .HasDatabaseName("UX_StudentNumberClaims_OnePendingPerStudent")
            .HasFilter($"[Status] = {(int)StudentNumberClaimStatus.Pending}")
            .IsUnique();
        builder.HasIndex(claim => new { claim.Status, claim.SubmittedAtUtc });
        builder.HasIndex(claim => new { claim.ClaimedStudentNumber, claim.Status });

        builder.HasOne(claim => claim.StudentProfile)
            .WithMany(profile => profile.StudentNumberClaims)
            .HasForeignKey(claim => claim.StudentProfileId);

        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_StudentNumberClaims_Status",
                "[Status] >= 0 AND [Status] <= 2");
            table.HasCheckConstraint(
                "CK_StudentNumberClaims_Review",
                "([Status] = 0 AND [ReviewedAtUtc] IS NULL AND [ReviewedByUserId] IS NULL) OR ([Status] IN (1, 2) AND [ReviewedAtUtc] IS NOT NULL AND [ReviewedByUserId] IS NOT NULL)");
        });
    }
}
