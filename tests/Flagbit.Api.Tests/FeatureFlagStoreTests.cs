using Flagbit.Core.Exceptions;
using Flagbit.Core.Models;
using Flagbit.Infrastructure;
using Flagbit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Flagbit.Api.Tests;

public sealed class FeatureFlagStoreTests
{
    [Fact]
    public async Task CrudOperationsPreserveCaseInsensitiveKeys()
    {
        var databaseName = $"flagbit-store-tests-{Guid.NewGuid()}";

        await using (var context = CreateContext(databaseName))
        {
            var store = new FeatureFlagStore(context);
            await store.AddAsync(new FeatureFlag("new-checkout", true, ["user-123"], 50));
        }

        await using (var context = CreateContext(databaseName))
        {
            var store = new FeatureFlagStore(context);
            var flag = await store.GetByKeyAsync("NEW-CHECKOUT");

            Assert.NotNull(flag);
            Assert.Equal("new-checkout", flag.Key);
            Assert.True(flag.IsEnabled);
            Assert.Equal(["user-123"], flag.TargetedUserIds);
            Assert.Equal(50, flag.RolloutPercentage);

            flag.Disable();
            await store.UpdateAsync(flag);
        }

        await using (var context = CreateContext(databaseName))
        {
            var store = new FeatureFlagStore(context);
            var flags = await store.GetAllAsync();
            var flag = Assert.Single(flags);

            Assert.False(flag.IsEnabled);
            Assert.True(await store.DeleteAsync("NEW-CHECKOUT"));
            Assert.False(await store.DeleteAsync("new-checkout"));
        }
    }

    [Fact]
    public async Task PostgreSqlUniqueViolationBecomesAlreadyExistsException()
    {
        var options = new DbContextOptionsBuilder<FlagbitDbContext>()
            .UseInMemoryDatabase($"flagbit-store-tests-{Guid.NewGuid()}")
            .AddInterceptors(new UniqueViolationInterceptor())
            .Options;
        await using var context = new FlagbitDbContext(options);
        var store = new FeatureFlagStore(context);

        var exception = await Assert.ThrowsAsync<FeatureFlagAlreadyExistsException>(async () =>
            await store.AddAsync(new FeatureFlag("new-checkout", false)));

        Assert.Equal("new-checkout", exception.Key);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    private static FlagbitDbContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<FlagbitDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new FlagbitDbContext(options);
    }

    private sealed class UniqueViolationInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var postgresException = new PostgresException("Duplicate key.", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation);
            throw new DbUpdateException("The database rejected the insert.", postgresException);
        }
    }
}
