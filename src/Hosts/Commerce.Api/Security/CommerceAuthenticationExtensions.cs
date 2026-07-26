using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Commerce.Api.Security;

public static class CommerceAuthenticationExtensions
{
    public static IServiceCollection AddCommerceAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var metadataAddress =
            GetRequiredSetting(
                configuration,
                "Authentication:MetadataAddress");

        var validIssuer =
            GetRequiredSetting(
                configuration,
                "Authentication:ValidIssuer");

        var audience =
            GetRequiredSetting(
                configuration,
                "Authentication:Audience");

        var requireHttpsMetadata =
            configuration.GetValue(
                "Authentication:RequireHttpsMetadata",
                defaultValue: true);

        services
            .AddAuthentication(
                JwtBearerDefaults
                    .AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MetadataAddress =
                    metadataAddress;

                options.RequireHttpsMetadata =
                    requireHttpsMetadata;

                options.MapInboundClaims = false;
                options.IncludeErrorDetails = false;

                options.TokenValidationParameters =
                    new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = validIssuer,

                        ValidateAudience = true,
                        ValidAudience = audience,

                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,

                        ClockSkew =
                            TimeSpan.FromMinutes(1),

                        NameClaimType = "sub"
                    };
            });

        services.AddAuthorization();

        return services;
    }

    private static string GetRequiredSetting(
        IConfiguration configuration,
        string key)
    {
        var value = configuration[key];

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Required configuration '{key}' is missing.");
        }

        return value;
    }
}
