using Catalog.Domain.Products;
using Catalog.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Infrastructure.Persistence.Configurations;

internal sealed class ProductRecordConfiguration :
    IEntityTypeConfiguration<ProductRecord>
{
    public void Configure(
        EntityTypeBuilder<ProductRecord> builder)
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

                tableBuilder.HasCheckConstraint(
                    "ck_products_version",
                    "version > 0");
            });

        builder.HasKey(
            product => product.Id)
            .HasName("pk_products");

        builder.Property(
                product => product.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(
                product => product.Name)
            .HasColumnName("name")
            .HasMaxLength(
                ProductName.MaximumLength)
            .IsRequired();

        builder.Property(
                product => product.Slug)
            .HasColumnName("slug")
            .HasMaxLength(
                ProductSlug.MaximumLength)
            .IsRequired();

        builder.Property(
                product => product.Description)
            .HasColumnName("description")
            .HasMaxLength(
                ProductDescription.MaximumLength)
            .IsRequired();

        builder.Property(
                product => product.Status)
            .HasColumnName("status")
            .HasMaxLength(32)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(
                product => product.PublishedAtUtc)
            .HasColumnName("published_at_utc")
            .HasColumnType(
                "timestamp with time zone");

        builder.Property(
                product => product.DiscontinuedAtUtc)
            .HasColumnName(
                "discontinued_at_utc")
            .HasColumnType(
                "timestamp with time zone");

        builder.Property(
                product => product.Version)
            .HasColumnName("version")
            .HasDefaultValue(1L)
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasIndex(
                product => product.Slug)
            .IsUnique()
            .HasDatabaseName(
                "ux_products_slug");

        builder.HasMany(
                product =>
                    product.OptionDefinitions)
            .WithOne()
            .HasForeignKey(
                option =>
                    option.ProductId)
            .OnDelete(
                DeleteBehavior.Restrict)
            .HasConstraintName(
                "fk_product_options_products");

        builder.HasMany(
                product =>
                    product.Variants)
            .WithOne()
            .HasForeignKey(
                variant =>
                    variant.ProductId)
            .OnDelete(
                DeleteBehavior.Restrict)
            .HasConstraintName(
                "fk_product_variants_products");
    }
}
