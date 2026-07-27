# Get Product by ID

`GET /api/catalog/products/{productId}`

Authorization: Bearer token

Policy: `ManageProductsPolicy` (`catalog.products.manage`)

Permission: `catalog.products.write`

## Responses

| Status | Meaning |
|---:|---|
| `200 OK` | Returns the administrative product details, including root state, options, variants, and variant selections. |
| `400 Bad Request` | `productId` is `Guid.Empty`. Error code: `Catalog.Product.InvalidId`. |
| `401 Unauthorized` | The caller is anonymous. |
| `403 Forbidden` | The authenticated caller does not have the `catalog.products.write` permission required by `ManageProductsPolicy`. |
| `404 Not Found` | The product does not exist. Error code: `Catalog.Product.NotFound`. |

## Caching

Successful responses include:

```http
Cache-Control: private, no-store
```

Administrative product details may contain Draft or Discontinued state and
must not be stored by shared or private caches.
