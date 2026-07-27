using MassTransit;

using Microsoft.EntityFrameworkCore;

namespace ConexaoSolidaria.Donations.Api.Data;

public sealed class DonationsDbContext(DbContextOptions<DonationsDbContext> options) : DbContext(options)
{
    public DbSet<Donation> Donations => Set<Donation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Donation>(builder =>
        {
            builder.ToTable("donations");
            builder.HasKey(donation => donation.Id);
            builder.Property(donation => donation.CampaignTitle).HasMaxLength(200).IsRequired();
            builder.Property(donation => donation.TenantId).HasMaxLength(80).IsRequired();
            builder.Property(donation => donation.DonorId).HasMaxLength(120).IsRequired();
            builder.Property(donation => donation.DonorEmail).HasMaxLength(180).IsRequired();
            builder.Property(donation => donation.Amount).HasPrecision(18, 2);
            builder.Property(donation => donation.Currency).HasMaxLength(3).IsRequired();
            builder.Property(donation => donation.PaymentMethod).HasMaxLength(32).IsRequired();
            builder.Property(donation => donation.Status).HasMaxLength(40).IsRequired();
            builder.HasIndex(donation => new { donation.TenantId, donation.CampaignId });
            builder.HasIndex(donation => new { donation.TenantId, donation.DonorId });
        });

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
