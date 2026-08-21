using Flagbit.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flagbit.Infrastructure.Persistence.Configurations;

internal sealed class FeatureFlagDependencyConfiguration : IEntityTypeConfiguration<FeatureFlagDependencyEntity>
{
    public void Configure(EntityTypeBuilder<FeatureFlagDependencyEntity> builder)
    {
        builder.ToTable("feature_flag_dependencies");

        builder.HasKey(dependency => dependency.Id)
            .HasName("pk_feature_flag_dependencies");

        builder.Property(dependency => dependency.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(dependency => dependency.FeatureFlagId)
            .HasColumnName("feature_flag_id");

        builder.Property(dependency => dependency.DependencyKey)
            .HasColumnName("dependency_key")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(dependency => dependency.NormalizedDependencyKey)
            .HasColumnName("normalized_dependency_key")
            .HasColumnType("text")
            .HasComputedColumnSql("upper(\"dependency_key\")", stored: true);

        builder.HasIndex(dependency => new { dependency.FeatureFlagId, dependency.NormalizedDependencyKey })
            .IsUnique()
            .HasDatabaseName("ux_feature_flag_dependencies_flag_key");

        builder.HasOne(dependency => dependency.FeatureFlag)
            .WithMany(featureFlag => featureFlag.Dependencies)
            .HasForeignKey(dependency => dependency.FeatureFlagId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
    }
}
