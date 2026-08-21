using Flagbit.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flagbit.Infrastructure.Persistence.Configurations;

internal sealed class FeatureFlagEnvironmentConfiguration : IEntityTypeConfiguration<FeatureFlagEnvironmentEntity>
{
    public void Configure(EntityTypeBuilder<FeatureFlagEnvironmentEntity> builder)
    {
        builder.ToTable("feature_flag_environments");

        builder.HasKey(environment => environment.Id)
            .HasName("pk_feature_flag_environments");

        builder.Property(environment => environment.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(environment => environment.FeatureFlagId)
            .HasColumnName("feature_flag_id");

        builder.Property(environment => environment.Name)
            .HasColumnName("name")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(environment => environment.NormalizedName)
            .HasColumnName("normalized_name")
            .HasColumnType("text")
            .HasComputedColumnSql("upper(\"name\")", stored: true);

        builder.HasIndex(environment => new { environment.FeatureFlagId, environment.NormalizedName })
            .IsUnique()
            .HasDatabaseName("ux_feature_flag_environments_flag_name");

        builder.HasOne(environment => environment.FeatureFlag)
            .WithMany(featureFlag => featureFlag.Environments)
            .HasForeignKey(environment => environment.FeatureFlagId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
    }
}
