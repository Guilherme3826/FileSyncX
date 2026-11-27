using FileSyncX.EfCore.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FileSyncX.EfCore.Configurations;

public sealed class FisicalFoldersConfiguration
    : IEntityTypeConfiguration<FisicalFolders>
{
    public void Configure(EntityTypeBuilder<FisicalFolders> builder)
    {
        builder.ToTable("tbl_fisical_folders", "public");

        builder.HasKey(x => x.Id)
            .HasName("PK_tbl_fisical_folders");

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.Name)
            .HasColumnName("name")
            .HasColumnType("text")
            .IsRequired();
    }
}