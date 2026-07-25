namespace Catalog.Api.Authorization;

public static class CatalogAuthorization
{
    public const string ManageProductsPolicy =
        "catalog.products.manage";

    public const string PermissionClaim =
        "permissions";

    public const string ProductsWritePermission =
        "catalog.products.write";
}
