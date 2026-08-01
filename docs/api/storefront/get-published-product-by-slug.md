# Get Published Product by Slug

`GET /api/storefront/products/{slug}`

Authentication: None. This is a public storefront endpoint.

The endpoint returns only Published products. Its response includes only
Active variants; Draft and Discontinued variants are not exposed.

## Responses

| Status | Meaning |
|---:|---|
| `200 OK` | Returns the published product and its Active variants. |
| `304 Not Modified` | `If-None-Match` matches the ETag generated from the current aggregate identity and version. The response does not contain a product body. |
| `400 Bad Request` | The route value is not a valid canonical product slug. Error code: `Catalog.Product.InvalidSlug`. |
| `404 Not Found` | The product is missing, Draft, or Discontinued. Error code: `Catalog.Storefront.ProductNotFound`. |

A canonical slug contains lowercase ASCII letters, numbers, and single
hyphens. It cannot begin or end with a hyphen.

## ETag

Successful responses include a weak ETag derived from the product identifier
and current aggregate version:

```http
ETag: W/"019fbbbcfa1e75a2935d0cc70c8cb5c7-1"
```

The format is:

```text
W/"{productId:N}-{version}"
```

Changing the aggregate version produces a different ETag.

## Conditional Request

Send the ETag from a previous response through `If-None-Match`:

```http
GET /api/storefront/products/enterprise-keyboard HTTP/1.1
Host: api.example.com
Accept: application/json
If-None-Match: W/"019fbbbcfa1e75a2935d0cc70c8cb5c7-1"
```

When the aggregate version has not changed, the endpoint responds without a
product body:

```http
HTTP/1.1 304 Not Modified
ETag: W/"019fbbbcfa1e75a2935d0cc70c8cb5c7-1"
Cache-Control: public, max-age=30, stale-while-revalidate=30
Vary: Accept-Encoding
```

## Cache-Control

Both `200 OK` and matching `304 Not Modified` responses include:

```http
Cache-Control: public, max-age=30, stale-while-revalidate=30
```

- `public` allows browsers and shared intermediaries to store the response.
- `max-age=30` makes the response fresh for 30 seconds.
- `stale-while-revalidate=30` allows a stale response to be served for another
  30 seconds while it is refreshed.