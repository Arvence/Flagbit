using Flagbit.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flagbit.Infrastructure.Persistence.Configurations;

internal sealed class FeatureFlagRuleConfiguration : IEntityTypeConfiguration<FeatureFlagRuleEntity>
{
    public void Configure(EntityTypeBuilder<FeatureFlagRuleEntity> builder)
    {
        builder.ToTable("feature_flag_rules", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_feature_flag_rules_position", "position >= 0");
        });

        builder.HasKey(rule => rule.Id)
            .HasName("pk_feature_flag_rules");

        builder.Property(rule => rule.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(rule => rule.FeatureFlagId)
            .HasColumnName("feature_flag_id");

        builder.Property(rule => rule.Position)
            .HasColumnName("position");

        builder.Property(rule => rule.Attribute)
            .HasColumnName("attribute")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(rule => rule.Operator)
            .HasColumnName("operator")
            .HasConversion<string>()
            .HasColumnType("text")
            .IsRequired();

        builder.Property(rule => rule.Value)
            .HasColumnName("value")
            .HasColumnType("text")
            .IsRequired();

        builder.HasIndex(rule => new { rule.FeatureFlagId, rule.Position })
            .IsUnique()
            .HasDatabaseName("ux_feature_flag_rules_flag_position");

        builder.HasOne(rule => rule.FeatureFlag)
            .WithMany(featureFlag => featureFlag.Rules)
            .HasForeignKey(rule => rule.FeatureFlagId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
    }
}
