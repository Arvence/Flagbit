using Flagbit.Api.Authentication;
using Flagbit.Api.Contracts;
using Flagbit.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Flagbit.Api;

public static class ApiKeyEndpoints
{
    public static IEndpointRouteBuilder MapApiKeyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/keys").WithTags("API keys").RequireAuthorization(ApiKeyOptions.ManagementPolicy);

        group.MapPost("", CreateAsync).Produces<CreatedApiKeyResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem().ProducesProblem(StatusCodes.Status415UnsupportedMediaType);
        group.MapGet("", GetAllAsync).Produces<ApiKeyResponse[]>();
        group.MapDelete("/{id:guid}", DeleteAsync).Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);

        group.WithMetadata(
            new ProducesResponseTypeMetadata(StatusCodes.Status401Unauthorized, typeof(void)),
            new ProducesResponseTypeMetadata(StatusCodes.Status403Forbidden, typeof(void)),
            new ProducesResponseTypeMetadata(StatusCodes.Status500InternalServerError, typeof(ProblemDetails), ["application/problem+json"]));

        return endpoints;
    }

    private static async Task<IResult> CreateAsync(CreateApiKeyRequest request, EvaluationApiKeyStore store, HttpContext context, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim();

        if (string.IsNullOrEmpty(name) || name.Length > 100)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = ["An API key name between 1 and 100 characters is required."]
            });
        }

        var secret = EvaluationApiKeySecret.Generate();
        var key = await store.AddAsync(name, EvaluationApiKeySecret.Hash(secret), cancellationToken);

        context.Response.Headers.CacheControl = "no-store";
        return Results.Json(new CreatedApiKeyResponse(key.Id, key.Name, key.CreatedAt, secret), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> GetAllAsync(EvaluationApiKeyStore store, CancellationToken cancellationToken)
    {
        var keys = await store.GetAllAsync(cancellationToken);
        return Results.Ok(keys.Select(key => new ApiKeyResponse(key.Id, key.Name, key.CreatedAt)));
    }

    private static async Task<IResult> DeleteAsync(Guid id, EvaluationApiKeyStore store, CancellationToken cancellationToken)
    {
        return await store.DeleteAsync(id, cancellationToken)
            ? Results.NoContent()
            : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "API key not found");
    }
}
