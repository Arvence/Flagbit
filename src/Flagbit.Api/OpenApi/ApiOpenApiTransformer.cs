using Flagbit.Api.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Flagbit.Api.OpenApi;

internal sealed class ApiOpenApiTransformer : IOpenApiDocumentTransformer, IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[ApiKeyAuthenticationHandler.SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = ApiKeyAuthenticationHandler.HeaderName,
            Description = "Use a management key for flag and application-key management. Evaluation accepts management, configured evaluation, or generated application keys."
        };
        return Task.CompletedTask;
    }

    public async Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        var authorization = metadata.OfType<IAuthorizeData>().ToArray();
        if (authorization.Length > 0 && !metadata.OfType<IAllowAnonymous>().Any())
        {
            operation.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(ApiKeyAuthenticationHandler.SchemeName, context.Document)] = [] }];
            operation.Description = authorization.Any(policy => policy.Policy == ApiKeyOptions.ManagementPolicy)
                ? "Requires a management API key."
                : "Accepts a management, configured evaluation, or generated application API key.";
        }

        if (context.Description.SupportedResponseTypes.Any(response => response.StatusCode == 400 && response.Type == typeof(HttpValidationProblemDetails)))
        {
            var ordinaryProblem = await context.GetOrCreateSchemaAsync(typeof(ProblemDetails), cancellationToken: cancellationToken);
            var validationProblem = operation.Responses!["400"].Content!["application/problem+json"].Schema!;
            operation.Responses["400"].Content!["application/problem+json"].Schema = new OpenApiSchema { AnyOf = [ordinaryProblem, validationProblem] };
        }
    }
}
