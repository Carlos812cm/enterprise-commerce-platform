using Catalog.Domain.Products;
using Catalog.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Infrastructure.Persistence.Configurations;

internal sealed class ProductOptionRecordConfiguration :
    IEntityTypeConfiguration<ProductOptionRecord>
{
    public void Configure(
        EntityTypeBuilder<ProductOptionRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "product_options",
            CatalogDbContext.Schema,
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    "ck_product_options_display_order",
                    "display_order >= 0");
            });

        builder.HasKey(
            option => option.Id)
            .HasName("pk_product_options");

        builder.HasAlternateKey(
                option => new
                {
                    option.ProductId,
                    option.Id
                })
            .HasName(
                "ak_product_options_product_id_id");

        builder.Property(
                option => option.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(
                option => option.ProductId)
            .HasColumnName("product_id")
            .HasColumnType("uuid");

        builder.Property(
                option => option.Name)
            .HasColumnName("name")
            .HasMaxLength(
                OptionName.MaximumLength)
            .IsRequired();

        builder.Property(
                option => option.NameKey)
            .HasColumnName("name_key")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(
                option => option.DisplayOrder)
            .HasColumnName("display_order")
            .IsRequired();

        builder.HasIndex(
                option => new
                {
                    option.ProductId,
                    option.NameKey
                })
            .IsUnique()
            .HasDatabaseName(
                "ux_product_options_product_name");

        builder.HasIndex(
                option => new
                {
                    option.ProductId,
                    option.DisplayOrder
                })
            .IsUnique()
            .HasDatabaseName(
                "ux_product_options_product_display_order");
    }
}
