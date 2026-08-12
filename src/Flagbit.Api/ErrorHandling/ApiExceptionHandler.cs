using System.Diagnostics;
using Flagbit.Core.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Flagbit.Api.ErrorHandling;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger, IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken _)
    {
        var (statusCode, title, detail) = MapException(exception);

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "An unhandled exception occurred while processing {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = statusCode;

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = httpContext.Request.Path
        };

        problemDetails.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problemDetails
        });

        return true;
    }

    private static (int StatusCode, string Title, string Detail) MapException(Exception exception)
    {
        return exception switch
        {
            FeatureFlagNotFoundException => (StatusCodes.Status404NotFound, "Feature flag not found", exception.Message),
            FeatureFlagAlreadyExistsException => (StatusCodes.Status409Conflict, "Feature flag already exists", exception.Message),
            ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request", exception.Message),
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "Invalid request", "The request could not be processed."),
            _ => (StatusCodes.Status500InternalServerError, "Internal server error", "An unexpected error occurred while processing the request.")
        };
    }
}
