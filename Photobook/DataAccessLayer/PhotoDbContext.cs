using Microsoft.EntityFrameworkCore;

namespace Photobook.DataAccessLayer;

public class PhotoDbContext : DbContext
{
    public DbSet<Photo> Photos { get; set; } = null!;

    public PhotoDbContext(DbContextOptions<PhotoDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Photo>(builder => 
        {
            builder.ToTable("Photos");

            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).ValueGeneratedNever();

            builder.Property(e => e.OriginalFileName).HasMaxLength(256).IsRequired();
            builder.Property(e => e.Path).HasMaxLength(512).IsRequired();
            builder.Property(e => e.Description).HasMaxLength(4000);
        });
    }
}
