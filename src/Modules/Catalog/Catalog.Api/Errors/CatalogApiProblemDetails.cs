using System.Diagnostics;
using Commerce.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Catalog.Api.Errors;

internal static class CatalogApiProblemDetails
{
    public static ProblemHttpResult Create(
        DomainError error,
        HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(httpContext);

        var mapping = GetHttpMapping(
            error.Type);

        var extensions =
            new Dictionary<string, object?>(
                StringComparer.Ordinal)
            {
                ["code"] = error.Code,
                ["traceId"] =
                    Activity.Current?.Id ??
                    httpContext.TraceIdentifier
            };

        return TypedResults.Problem(
            detail: error.Description,
            instance: httpContext.Request.Path,
            statusCode: mapping.StatusCode,
            title: mapping.Title,
            type: $"urn:commerce:error:{error.Code}",
            extensions: extensions);
    }

    private static (
        int StatusCode,
        string Title)
        GetHttpMapping(ErrorType errorType)
    {
        return errorType switch
        {
            ErrorType.Validation =>
                (
                    StatusCodes.Status400BadRequest,
                    "Request validation failed."
                ),

            ErrorType.NotFound =>
                (
                    StatusCodes.Status404NotFound,
                    "The requested resource was not found."
                ),

            ErrorType.Conflict =>
                (
                    StatusCodes.Status409Conflict,
                    "The request conflicts with the current state."
                ),

            ErrorType.Failure =>
                (
                    StatusCodes.Status500InternalServerError,
                    "The operation could not be completed."
                ),

            _ =>
                (
                    StatusCodes.Status500InternalServerError,
                    "An unexpected error occurred."
                )
        };
    }
}
