using Flagbit.Api.Authentication;
using Flagbit.Api.Contracts;
using Flagbit.Core.Models;
using Flagbit.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace Flagbit.Api;

public static class FeatureFlagEndpoints
{
    public static IEndpointRouteBuilder MapFeatureFlagEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/flags").WithTags("Feature flags").RequireAuthorization(ApiKeyOptions.EvaluationPolicy);

        group.MapGet("", GetAllAsync).RequireAuthorization(ApiKeyOptions.ManagementPolicy)
            .Produces<FeatureFlagResponse[]>();
        group.MapGet("/{key}/enabled", IsEnabledAsync).Produces<FeatureFlagEvaluationResponse>();
        group.MapGet("/{key}", GetByKeyAsync).RequireAuthorization(ApiKeyOptions.ManagementPolicy)
            .Produces<FeatureFlagResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("", CreateAsync).RequireAuthorization(ApiKeyOptions.ManagementPolicy)
            .Produces<FeatureFlagResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem().ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status415UnsupportedMediaType);
        group.MapPost("/{key}/evaluate", EvaluateAsync).Produces<FeatureFlagEvaluationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status415UnsupportedMediaType);
        group.MapPut("/{key}/evaluation", UpdateEvaluationAsync).RequireAuthorization(ApiKeyOptions.ManagementPolicy)
            .Produces<FeatureFlagResponse>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);
        group.MapPut("/{key}/enable", EnableAsync).RequireAuthorization(ApiKeyOptions.ManagementPolicy)
            .Produces<FeatureFlagResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPut("/{key}/disable", DisableAsync).RequireAuthorization(ApiKeyOptions.ManagementPolicy)
            .Produces<FeatureFlagResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapDelete("/{key}", DeleteAsync).RequireAuthorization(ApiKeyOptions.ManagementPolicy)
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);

        group.WithMetadata(
            new ProducesResponseTypeMetadata(StatusCodes.Status401Unauthorized, typeof(void)),
            new ProducesResponseTypeMetadata(StatusCodes.Status403Forbidden, typeof(void)),
            new ProducesResponseTypeMetadata(StatusCodes.Status500InternalServerError, typeof(ProblemDetails), ["application/problem+json"]));

        return endpoints;
    }

    private static async Task<IResult> GetAllAsync(FeatureFlagManager manager, CancellationToken cancellationToken)
    {
        var flags = await manager.GetAllAsync(cancellationToken);
        return Results.Ok(flags.Select(FeatureFlagResponse.From));
    }

    private static async Task<IResult> GetByKeyAsync(string key, FeatureFlagManager manager, CancellationToken cancellationToken)
    {
        var flag = await manager.GetByKeyAsync(key, cancellationToken);
        return Results.Ok(FeatureFlagResponse.From(flag));
    }

    private static async Task<IResult> IsEnabledAsync(string key, string? userId, FeatureFlagEvaluator evaluator, CancellationToken cancellationToken)
    {
        var isEnabled = await evaluator.IsEnabledAsync(key, new FeatureFlagContext(UserId: userId), cancellationToken);
        return Results.Ok(new FeatureFlagEvaluationResponse(key, isEnabled));
    }

    private static async Task<IResult> EvaluateAsync(string key, EvaluateFeatureFlagRequest request, FeatureFlagEvaluator evaluator, CancellationToken cancellationToken)
    {
        var context = new FeatureFlagContext(request.UserId, request.Environment, Attributes: request.Attributes);
        var isEnabled = await evaluator.IsEnabledAsync(key, context, cancellationToken);
        return Results.Ok(new FeatureFlagEvaluationResponse(key, isEnabled));
    }

    private static async Task<IResult> CreateAsync(CreateFeatureFlagRequest request, FeatureFlagManager manager, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Key))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["key"] = ["A feature flag key is required."]
            });
        }

        if (request.Key is "." or ".." || request.Key.Contains('/') || request.Key.Contains('\0'))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["key"] = ["A feature flag key cannot be '.' or '..', or contain '/' or a null character."]
            });
        }

        var rules = MapRules(request.Rules);
        var flag = await manager.CreateAsync(request.Key, request.IsEnabled, request.TargetedUserIds, request.RolloutPercentage, request.Environments, rules, request.StartsAt, request.EndsAt, request.DependencyKeys, cancellationToken);
        var location = $"/api/flags/{Uri.EscapeDataString(flag.Key)}";
        return Results.Created(location, FeatureFlagResponse.From(flag));
    }

    private static async Task<IResult> UpdateEvaluationAsync(string key, UpdateFeatureFlagEvaluationRequest request, FeatureFlagManager manager, CancellationToken cancellationToken)
    {
        var rules = MapRules(request.Rules);
        var flag = await manager.UpdateEvaluationAsync(key, request.TargetedUserIds, request.RolloutPercentage, request.Environments, rules, request.StartsAt, request.EndsAt, request.DependencyKeys, cancellationToken);
        return Results.Ok(FeatureFlagResponse.From(flag));
    }

    private static Task<IResult> EnableAsync(string key, FeatureFlagManager manager, CancellationToken cancellationToken)
    {
        return ChangeStateAsync(key, manager.EnableAsync, cancellationToken);
    }

    private static Task<IResult> DisableAsync(string key, FeatureFlagManager manager, CancellationToken cancellationToken)
    {
        return ChangeStateAsync(key, manager.DisableAsync, cancellationToken);
    }

    private static async Task<IResult> DeleteAsync(string key, FeatureFlagManager manager, CancellationToken cancellationToken)
    {
        await manager.DeleteAsync(key, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> ChangeStateAsync(string key, Func<string, CancellationToken, ValueTask<FeatureFlag>> changeState, CancellationToken cancellationToken)
    {
        var flag = await changeState(key, cancellationToken);
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
