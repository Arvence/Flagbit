using Flagbit.Api.Contracts;
using Flagbit.Core.Models;
using Flagbit.Core.Services;

namespace Flagbit.Api;

public static class FeatureFlagEndpoints
{
    public static IEndpointRouteBuilder MapFeatureFlagEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/flags").WithTags("Feature flags");

        group.MapGet("", GetAllAsync);
        group.MapGet("/{key}/enabled", IsEnabledAsync);
        group.MapGet("/{key}", GetByKeyAsync);
        group.MapPost("", CreateAsync);
        group.MapPost("/{key}/evaluate", EvaluateAsync);
        group.MapPut("/{key}/evaluation", UpdateEvaluationAsync);
        group.MapPut("/{key}/enable", EnableAsync);
        group.MapPut("/{key}/disable", DisableAsync);
        group.MapDelete("/{key}", DeleteAsync);

        return endpoints;
    }

    private static async Task<IResult> GetAllAsync(FeatureFlagManager manager)
    {
        var flags = await manager.GetAllAsync();
        return Results.Ok(flags.Select(FeatureFlagResponse.From));
    }

    private static async Task<IResult> GetByKeyAsync(string key, FeatureFlagManager manager)
    {
        var flag = await manager.GetByKeyAsync(key);
        return Results.Ok(FeatureFlagResponse.From(flag));
    }

    private static async Task<IResult> IsEnabledAsync(string key, string? userId, FeatureFlagEvaluator evaluator)
    {
        var isEnabled = await evaluator.IsEnabledAsync(key, new FeatureFlagContext(UserId: userId));
        return Results.Ok(new FeatureFlagEvaluationResponse(key, isEnabled));
    }

    private static async Task<IResult> EvaluateAsync(string key, EvaluateFeatureFlagRequest request, FeatureFlagEvaluator evaluator)
    {
        var context = new FeatureFlagContext(request.UserId, request.Environment, Attributes: request.Attributes);
        var isEnabled = await evaluator.IsEnabledAsync(key, context);
        return Results.Ok(new FeatureFlagEvaluationResponse(key, isEnabled));
    }

    private static async Task<IResult> CreateAsync(CreateFeatureFlagRequest request, FeatureFlagManager manager)
    {
        if (string.IsNullOrWhiteSpace(request.Key))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["key"] = ["A feature flag key is required."]
            });
        }

        var rules = MapRules(request.Rules);
        var flag = await manager.CreateAsync(request.Key, request.IsEnabled, request.TargetedUserIds, request.RolloutPercentage, request.Environments, rules, request.StartsAt, request.EndsAt, request.DependencyKeys);
        var location = $"/api/flags/{Uri.EscapeDataString(flag.Key)}";
        return Results.Created(location, FeatureFlagResponse.From(flag));
    }

    private static async Task<IResult> UpdateEvaluationAsync(string key, UpdateFeatureFlagEvaluationRequest request, FeatureFlagManager manager)
    {
        var rules = MapRules(request.Rules);
        var flag = await manager.UpdateEvaluationAsync(key, request.TargetedUserIds, request.RolloutPercentage, request.Environments, rules, request.StartsAt, request.EndsAt, request.DependencyKeys);
        return Results.Ok(FeatureFlagResponse.From(flag));
    }

    private static Task<IResult> EnableAsync(string key, FeatureFlagManager manager)
    {
        return ChangeStateAsync(key, manager.EnableAsync);
    }

    private static Task<IResult> DisableAsync(string key, FeatureFlagManager manager)
    {
        return ChangeStateAsync(key, manager.DisableAsync);
    }

    private static async Task<IResult> DeleteAsync(string key, FeatureFlagManager manager)
    {
        await manager.DeleteAsync(key);
        return Results.NoContent();
    }

    private static async Task<IResult> ChangeStateAsync(string key, Func<string, ValueTask<FeatureFlag>> changeState)
    {
        var flag = await changeState(key);
        return Results.Ok(FeatureFlagResponse.From(flag));
    }

    private static IReadOnlyCollection<FeatureFlagRule>? MapRules(IReadOnlyCollection<FeatureFlagRuleRequest>? rules)
    {
        if (rules is null)
        {
            return null;
        }

        var mappedRules = new List<FeatureFlagRule>(rules.Count);

        foreach (var rule in rules)
        {
            mappedRules.Add(rule?.ToDomain() ?? throw new ArgumentException("Evaluation rules cannot contain null entries.", nameof(rules)));
        }

        return mappedRules;
    }
}
