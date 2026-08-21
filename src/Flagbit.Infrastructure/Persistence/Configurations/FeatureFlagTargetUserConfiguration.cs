using Flagbit.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flagbit.Infrastructure.Persistence.Configurations;

internal sealed class FeatureFlagTargetUserConfiguration : IEntityTypeConfiguration<FeatureFlagTargetUserEntity>
{
    public void Configure(EntityTypeBuilder<FeatureFlagTargetUserEntity> builder)
    {
        builder.ToTable("feature_flag_target_users");

        builder.HasKey(targetUser => targetUser.Id)
            .HasName("pk_feature_flag_target_users");

        builder.Property(targetUser => targetUser.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(targetUser => targetUser.FeatureFlagId)
            .HasColumnName("feature_flag_id");

        builder.Property(targetUser => targetUser.UserId)
            .HasColumnName("user_id")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(targetUser => targetUser.NormalizedUserId)
            .HasColumnName("normalized_user_id")
            .HasColumnType("text")
            .HasComputedColumnSql("upper(\"user_id\")", stored: true);

        builder.HasIndex(targetUser => new { targetUser.FeatureFlagId, targetUser.NormalizedUserId })
            .IsUnique()
            .HasDatabaseName("ux_feature_flag_target_users_flag_user");

        builder.HasOne(targetUser => targetUser.FeatureFlag)
            .WithMany(featureFlag => featureFlag.TargetUsers)
            .HasForeignKey(targetUser => targetUser.FeatureFlagId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
    }
}
