using Catalog.Domain.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Infrastructure.Persistence.Configurations;

internal sealed class ProductConfiguration :
    IEntityTypeConfiguration<Product>
{
    public void Configure(
        EntityTypeBuilder<Product> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "products",
            CatalogDbContext.Schema,
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    "ck_products_status",
                    "status IN ('Draft', 'Published', 'Discontinued')");

                tableBuilder.HasCheckConstraint(
                    "ck_products_lifecycle",
                    """
                    (
                        status = 'Draft'
                        AND published_at_utc IS NULL
                        AND discontinued_at_utc IS NULL
                    )
                    OR
                    (
                        status = 'Published'
                        AND published_at_utc IS NOT NULL
                        AND discontinued_at_utc IS NULL
                    )
                    OR
                    (
                        status = 'Discontinued'
                        AND discontinued_at_utc IS NOT NULL
                    )
                    """);

                tableBuilder.HasCheckConstraint(
                    "ck_products_timestamp_order",
                    """
                    published_at_utc IS NULL
                    OR discontinued_at_utc IS NULL
                    OR discontinued_at_utc >= published_at_utc
                    """);
            });

        builder.HasKey(product => product.Id)
            .HasName("pk_products");

        builder.Property(product => product.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .HasConversion(
                productId => productId.Value,
                value => ProductId.Create(value))
            .ValueGeneratedNever();

        builder.Property(product => product.Name)
            .HasColumnName("name")
            .HasMaxLength(ProductName.MaximumLength)
            .HasConversion(
                name => name.Value,
                value =>
                    CatalogValueObjectPersistence
                        .CreateProductName(value))
            .IsRequired();

        builder.Property(product => product.Slug)
            .HasColumnName("slug")
            .HasMaxLength(ProductSlug.MaximumLength)
            .HasConversion(
                slug => slug.Value,
                value =>
                    CatalogValueObjectPersistence
                        .CreateProductSlug(value))
            .IsRequired();

        builder.Property(product => product.Description)
            .HasColumnName("description")
            .HasMaxLength(ProductDescription.MaximumLength)
            .HasConversion(
                description => description.Value,
                value =>
                    CatalogValueObjectPersistence
                        .CreateProductDescription(value))
            .IsRequired();

        builder.Property(product => product.Status)
            .HasColumnName("status")
            .HasMaxLength(32)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(product => product.PublishedAtUtc)
            .HasColumnName("published_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(product => product.DiscontinuedAtUtc)
            .HasColumnName("discontinued_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(product => product.Slug)
            .IsUnique()
            .HasDatabaseName("ux_products_slug");

        builder.Ignore(product => product.OptionDefinitions);
        builder.Ignore(product => product.Variants);
        builder.Ignore(product => product.DomainEvents);
    }
}
