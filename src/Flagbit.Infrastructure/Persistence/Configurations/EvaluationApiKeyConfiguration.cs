using Flagbit.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flagbit.Infrastructure.Persistence.Configurations;

internal sealed class EvaluationApiKeyConfiguration : IEntityTypeConfiguration<EvaluationApiKeyEntity>
{
    public void Configure(EntityTypeBuilder<EvaluationApiKeyEntity> builder)
    {
        builder.ToTable("evaluation_api_keys");
        builder.HasKey(key => key.Id).HasName("pk_evaluation_api_keys");
        builder.Property(key => key.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(key => key.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(key => key.KeyHash).HasColumnName("key_hash").HasMaxLength(64).IsRequired();
        builder.Property(key => key.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        builder.HasIndex(key => key.KeyHash).IsUnique().HasDatabaseName("ux_evaluation_api_keys_key_hash");
    }
}
