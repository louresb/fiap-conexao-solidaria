using MassTransit;

using Microsoft.EntityFrameworkCore;

namespace ConexaoSolidaria.Payments.Api.Data;

public sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options)
{
    public DbSet<PaymentIntent> Payments => Set<PaymentIntent>();
    public DbSet<PaymentWebhook> Webhooks => Set<PaymentWebhook>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<PaymentIntent>(entity =>
        {
            entity.ToTable("payment_intents");
            entity.HasKey(payment => payment.Id);
            entity.Property(payment => payment.TenantId).HasMaxLength(80).IsRequired();
            entity.Property(payment => payment.Amount).HasPrecision(18, 2);
            entity.Property(payment => payment.Currency).HasMaxLength(3).IsRequired();
            entity.Property(payment => payment.PaymentMethod).HasMaxLength(32).IsRequired();
            entity.Property(payment => payment.Provider).HasMaxLength(64).IsRequired();
            entity.Property(payment => payment.ProviderPaymentId).HasMaxLength(160).IsRequired();
            entity.Property(payment => payment.Status).HasMaxLength(40).IsRequired();
            entity.Property(payment => payment.QrCodePayload).HasMaxLength(1024);
            entity.HasIndex(payment => new { payment.TenantId, payment.DonationId }).IsUnique();
            entity.HasIndex(payment => payment.ProviderPaymentId).IsUnique();
        });

        modelBuilder.Entity<PaymentWebhook>(entity =>
        {
            entity.ToTable("payment_webhooks");
            entity.HasKey(webhook => webhook.Id);
            entity.Property(webhook => webhook.ProviderEventId).HasMaxLength(160).IsRequired();
            entity.Property(webhook => webhook.Status).HasMaxLength(40).IsRequired();
            entity.HasIndex(webhook => webhook.ProviderEventId).IsUnique();
        });

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}