using Catalog.Domain.Products;
using Catalog.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Infrastructure.Persistence.Configurations;

internal sealed class ProductVariantRecordConfiguration :
    IEntityTypeConfiguration<ProductVariantRecord>
{
    public void Configure(
        EntityTypeBuilder<ProductVariantRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "product_variants",
            CatalogDbContext.Schema,
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    "ck_product_variants_status",
                    "status IN ('Draft', 'Active', 'Discontinued')");

                tableBuilder.HasCheckConstraint(
                    "ck_product_variants_lifecycle",
                    """
                    (
                        status = 'Draft'
                        AND activated_at_utc IS NULL
                        AND discontinued_at_utc IS NULL
                    )
                    OR
                    (
                        status = 'Active'
                        AND activated_at_utc IS NOT NULL
                        AND discontinued_at_utc IS NULL
                    )
                    OR
                    (
                        status = 'Discontinued'
                        AND discontinued_at_utc IS NOT NULL
                    )
                    """);

                tableBuilder.HasCheckConstraint(
                    "ck_product_variants_timestamp_order",
                    """
                    activated_at_utc IS NULL
                    OR discontinued_at_utc IS NULL
                    OR discontinued_at_utc >= activated_at_utc
                    """);
            });

        builder.HasKey(
            variant => variant.Id)
            .HasName("pk_product_variants");

        builder.HasAlternateKey(
                variant => new
                {
                    variant.ProductId,
                    variant.Id
                })
            .HasName(
                "ak_product_variants_product_id_id");

        builder.Property(
                variant => variant.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(
                variant => variant.ProductId)
            .HasColumnName("product_id")
            .HasColumnType("uuid");

        builder.Property(
                variant => variant.Sku)
            .HasColumnName("sku")
            .HasMaxLength(
                Sku.MaximumLength)
            .IsRequired();

        builder.Property(
                variant => variant.Status)
            .HasColumnName("status")
            .HasMaxLength(32)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(
                variant =>
                    variant.OptionCombinationKey)
            .HasColumnName(
                "option_combination_key")
            .HasColumnType("character(64)")
            .IsRequired();

        builder.Property(
                variant => variant.ActivatedAtUtc)
            .HasColumnName("activated_at_utc")
            .HasColumnType(
                "timestamp with time zone");

        builder.Property(
                variant =>
                    variant.DiscontinuedAtUtc)
            .HasColumnName(
                "discontinued_at_utc")
            .HasColumnType(
                "timestamp with time zone");

        builder.HasIndex(
                variant => variant.Sku)
            .IsUnique()
            .HasDatabaseName(
                "ux_product_variants_sku");

        builder.HasIndex(
                variant => new
                {
                    variant.ProductId,
                    variant.OptionCombinationKey
                })
            .IsUnique()
            .HasFilter(
                "status <> 'Discontinued'")
            .HasDatabaseName(
                "ux_product_variants_product_combination_live");
    }
}
