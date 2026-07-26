using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace ConexaoSolidaria.Identity.Api.Data;

public sealed class IdentityDbContext : DbContext
{
    public IdentityDbContext(DbContextOptions<IdentityDbContext> options) : base(options)
    {
    }

    public DbSet<Donor> Donors => Set<Donor>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Donor>(builder =>
        {
            builder.ToTable("donors");
            builder.HasKey(d => d.Id);
            builder.Property(d => d.TenantId).HasMaxLength(80).IsRequired();
            builder.Property(d => d.FullName).HasMaxLength(180).IsRequired();
            builder.Property(d => d.Email).HasMaxLength(180).IsRequired();
            builder.Property(d => d.Cpf).HasMaxLength(11).IsRequired();
            builder.Property(d => d.PasswordHash).HasMaxLength(256).IsRequired();
            builder.HasIndex(d => new { d.TenantId, d.Email }).IsUnique();
            builder.HasIndex(d => new { d.TenantId, d.Cpf }).IsUnique();
        });

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
