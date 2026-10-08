using Flagbit.Core.Models;
using Flagbit.Infrastructure.Persistence.Entities;

namespace Flagbit.Infrastructure.Persistence;

internal static class FeatureFlagEntityMapper
{
    public static FeatureFlag ToDomain(FeatureFlagEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var targetedUserIds = entity.TargetUsers
            .OrderBy(targetUser => targetUser.Id)
            .Select(targetUser => targetUser.UserId);
        var environments = entity.Environments
            .OrderBy(environment => environment.Id)
            .Select(environment => environment.Name);
        var rules = entity.Rules
            .OrderBy(rule => rule.Position)
            .Select(rule => new FeatureFlagRule(rule.Attribute, rule.Operator, rule.Value));
        var dependencyKeys = entity.Dependencies
            .OrderBy(dependency => dependency.Id)
            .Select(dependency => dependency.DependencyKey);

        return new FeatureFlag(entity.Key, entity.IsEnabled, targetedUserIds, entity.RolloutPercentage, environments, rules, entity.StartsAt, entity.EndsAt, dependencyKeys);
    }

    public static FeatureFlagEntity ToEntity(FeatureFlag flag)
    {
        ArgumentNullException.ThrowIfNull(flag);

        var entity = new FeatureFlagEntity();

        entity.Key = flag.Key;
        entity.NormalizedKey = FeatureFlagIdentifier.Normalize(flag.Key);
        entity.IsEnabled = flag.IsEnabled;
        entity.RolloutPercentage = flag.RolloutPercentage;
        entity.StartsAt = flag.StartsAt;
        entity.EndsAt = flag.EndsAt;

        foreach (var userId in flag.TargetedUserIds)
        {
            entity.TargetUsers.Add(new FeatureFlagTargetUserEntity
            {
                UserId = userId,
                NormalizedUserId = FeatureFlagIdentifier.Normalize(userId),
                FeatureFlag = entity
            });
        }

        foreach (var environment in flag.Environments)
        {
            entity.Environments.Add(new FeatureFlagEnvironmentEntity
            {
                Name = environment,
                NormalizedName = FeatureFlagIdentifier.Normalize(environment),
                FeatureFlag = entity
            });
        }

        var position = 0;
        foreach (var rule in flag.Rules)
        {
            entity.Rules.Add(new FeatureFlagRuleEntity
            {
                Position = position++,
                Attribute = rule.Attribute,
                Operator = rule.Operator,
                Value = rule.Value,
                FeatureFlag = entity
            });
        }

        foreach (var dependencyKey in flag.DependencyKeys)
        {
            entity.Dependencies.Add(new FeatureFlagDependencyEntity
            {
                DependencyKey = dependencyKey,
                NormalizedDependencyKey = FeatureFlagIdentifier.Normalize(dependencyKey),
                FeatureFlag = entity
            });
        }

        return entity;
    }
}
