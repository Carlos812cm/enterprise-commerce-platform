using System.Diagnostics;

namespace Commerce.Api.Errors;

public static class ApiProblemDetailsExtensions
{
    public static IServiceCollection AddApiProblemDetails(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails =
                context =>
                {
                    context.ProblemDetails.Instance ??=
                        context.HttpContext
                            .Request
                            .Path;

                    context
                        .ProblemDetails
                        .Extensions["traceId"] =
                        Activity.Current?.Id ??
                        context.HttpContext
                            .TraceIdentifier;
                };
        });

        return services;
    }
}
