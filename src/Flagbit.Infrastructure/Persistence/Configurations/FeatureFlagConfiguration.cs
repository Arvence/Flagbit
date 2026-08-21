using Flagbit.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flagbit.Infrastructure.Persistence.Configurations;

internal sealed class FeatureFlagConfiguration : IEntityTypeConfiguration<FeatureFlagEntity>
{
    public void Configure(EntityTypeBuilder<FeatureFlagEntity> builder)
    {
        builder.ToTable("feature_flags", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_feature_flags_rollout_percentage", "rollout_percentage IS NULL OR rollout_percentage BETWEEN 0 AND 100");
            tableBuilder.HasCheckConstraint("ck_feature_flags_schedule", "starts_at IS NULL OR ends_at IS NULL OR starts_at <= ends_at");
        });

        builder.HasKey(featureFlag => featureFlag.Id)
            .HasName("pk_feature_flags");

        builder.Property(featureFlag => featureFlag.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(featureFlag => featureFlag.Key)
            .HasColumnName("key")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(featureFlag => featureFlag.NormalizedKey)
            .HasColumnName("normalized_key")
            .HasColumnType("text")
            .HasComputedColumnSql("upper(\"key\")", stored: true);

        builder.Property(featureFlag => featureFlag.IsEnabled)
            .HasColumnName("is_enabled")
            .IsRequired();

        builder.Property(featureFlag => featureFlag.RolloutPercentage)
            .HasColumnName("rollout_percentage");

        builder.Property(featureFlag => featureFlag.StartsAt)
            .HasColumnName("starts_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(featureFlag => featureFlag.EndsAt)
            .HasColumnName("ends_at")
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(featureFlag => featureFlag.NormalizedKey)
            .IsUnique()
            .HasDatabaseName("ux_feature_flags_normalized_key");
    }
}
