using Lodestone.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lodestone.Infrastructure.Data.Configurations;

public sealed class SupportInteractionConfiguration : IEntityTypeConfiguration<SupportInteraction>
{
    public void Configure(EntityTypeBuilder<SupportInteraction> builder)
    {
        builder.Property(interaction => interaction.VolunteerUserId).HasMaxLength(450);
        builder.Property(interaction => interaction.StudentUserId).HasMaxLength(450);
        builder.Property(interaction => interaction.Message).IsRequired().HasMaxLength(2000);

        builder.HasIndex(interaction => new { interaction.SupportRequestId, interaction.CreatedAtUtc });
        builder.HasIndex(interaction => new { interaction.VolunteerUserId, interaction.Type });

        builder.HasOne(interaction => interaction.SupportRequest)
            .WithMany(request => request.Interactions)
            .HasForeignKey(interaction => interaction.SupportRequestId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
