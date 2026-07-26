using System.Security.Claims;
using System.Text.Encodings.Web;
using Catalog.Api.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Commerce.Api.IntegrationTests.Security;

internal sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder urlEncoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(
        options,
        loggerFactory,
        urlEncoder)
{
    public const string SchemeName =
        "IntegrationTest";

    public const string PermissionHeader =
        "X-Test-Permission";

    protected override Task<AuthenticateResult>
        HandleAuthenticateAsync()
    {
        var authorization =
            Request.Headers.Authorization
                .ToString();

        if (!authorization.StartsWith(
                SchemeName,
                StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(
                AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new(
                ClaimTypes.NameIdentifier,
                "integration-test-client")
        };

        if (string.Equals(
                Request.Headers[PermissionHeader],
                CatalogAuthorization
                    .ProductsWritePermission,
                StringComparison.Ordinal))
        {
            claims.Add(
                new Claim(
                    CatalogAuthorization
                        .PermissionClaim,
                    CatalogAuthorization
                        .ProductsWritePermission));
        }

        var principal =
            new ClaimsPrincipal(
                new ClaimsIdentity(
                    claims,
                    SchemeName));

        var ticket =
            new AuthenticationTicket(
                principal,
                SchemeName);

        return Task.FromResult(
            AuthenticateResult.Success(
                ticket));
    }
}
