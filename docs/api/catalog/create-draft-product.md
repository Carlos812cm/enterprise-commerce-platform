POST /api/catalog/products
Authorization: Bearer token
Permission: catalog.products.write
Success: 201
Location: /api/catalog/products/{productId}
Validation: 400
Unauthorized: 401
Forbidden: 403
Duplicate slug: 409

The `Location` returned by a successful POST can now be queried with:

`GET /api/catalog/products/{productId}`

The GET requires the same Bearer token and `catalog.products.write` permission
through `ManageProductsPolicy`.
