using Lodestone.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lodestone.Infrastructure.Data.Configurations;

public sealed class RiskMonitoringConsentHistoryConfiguration : IEntityTypeConfiguration<RiskMonitoringConsentHistory>
{
    public void Configure(EntityTypeBuilder<RiskMonitoringConsentHistory> builder)
    {
        builder.Property(history => history.PolicyVersion).IsRequired().HasMaxLength(32);
        builder.Property(history => history.ChangedByUserId).HasMaxLength(450);
        builder.HasIndex(history => new { history.StudentProfileId, history.ChangedAtUtc });
        builder.HasOne(history => history.StudentProfile)
            .WithMany(profile => profile.RiskMonitoringConsentHistory)
            .HasForeignKey(history => history.StudentProfileId);
    }
}
