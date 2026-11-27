using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using FileSyncX.EfCore.Models;

namespace FileSyncX.EfCore.Configurations;

public sealed class DefaultFoldersClassificationConfiguration
    : IEntityTypeConfiguration<DefaultFoldersClassification>
{
    public void Configure(
        EntityTypeBuilder<DefaultFoldersClassification> builder)
    {
        // TABELA
        builder.ToTable(
            "tbl_default_folders_classification",
            "public");

        // PRIMARY KEY
        builder.HasKey(x => x.Id)
            .HasName("pk_tbl_default_folders_classification");

        // IDENTITY
        builder.Property(x => x.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        // FOLDER NAME
        builder.Property(x => x.FolderName)
            .HasColumnName("folder_name")
            .HasColumnType("varchar(255)")
            .IsRequired();

        // CREATED AT
        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        // CLASSIFIED AT
        builder.Property(x => x.ClassifiedAt)
            .HasColumnName("classified_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        // UNIQUE INDEX
        builder.HasIndex(x => x.FolderName)
            .IsUnique()
            .HasDatabaseName(
                "uq_tbl_default_folders_classification_folder_name");
    }
}