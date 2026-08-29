using Lodestone.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lodestone.Infrastructure.Data.Configurations;

public sealed class RiskMonitoringConsentConfiguration : IEntityTypeConfiguration<RiskMonitoringConsent>
{
    public void Configure(EntityTypeBuilder<RiskMonitoringConsent> builder)
    {
        builder.Property(consent => consent.PolicyVersion).IsRequired().HasMaxLength(32);
        builder.HasIndex(consent => consent.StudentProfileId).IsUnique();
        builder.HasOne(consent => consent.StudentProfile)
            .WithOne(profile => profile.RiskMonitoringConsent)
            .HasForeignKey<RiskMonitoringConsent>(consent => consent.StudentProfileId);

        builder.ToTable(table => table.HasCheckConstraint(
            "CK_RiskMonitoringConsents_State",
            "[IsConsented] = 0 OR ([ConsentedAtUtc] IS NOT NULL AND [WithdrawnAtUtc] IS NULL)"));
    }
}
