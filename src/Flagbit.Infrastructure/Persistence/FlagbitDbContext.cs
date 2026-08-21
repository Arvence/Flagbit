using Flagbit.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flagbit.Infrastructure.Persistence;

public sealed class FlagbitDbContext : DbContext
{
    public FlagbitDbContext(DbContextOptions<FlagbitDbContext> options) : base(options)
    {
    }

    internal DbSet<FeatureFlagEntity> FeatureFlags => Set<FeatureFlagEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FlagbitDbContext).Assembly);
    }
}
