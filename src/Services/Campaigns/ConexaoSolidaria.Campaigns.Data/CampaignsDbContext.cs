using ConexaoSolidaria.Contracts.Campaigns;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace ConexaoSolidaria.Campaigns.Data;

public sealed class CampaignsDbContext : DbContext
{
    public CampaignsDbContext(DbContextOptions<CampaignsDbContext> options) : base(options)
    {
    }

    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<Donation> Donations => Set<Donation>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Campaign>(builder =>
        {
            builder.ToTable("campaigns");
            builder.HasKey(c => c.Id);
            builder.Property(c => c.TenantId).HasMaxLength(80).IsRequired();
            builder.Property(c => c.Title).HasMaxLength(160).IsRequired();
            builder.Property(c => c.Description).HasMaxLength(2000).IsRequired();
            builder.Property(c => c.GoalAmount).HasPrecision(18, 2);
            builder.Property(c => c.TotalRaised).HasPrecision(18, 2);
            builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(32);
            builder.HasIndex(c => new { c.TenantId, c.Status });
        });

        modelBuilder.Entity<Donation>(builder =>
        {
            builder.ToTable("donations");
            builder.HasKey(d => d.Id);
            builder.Property(d => d.TenantId).HasMaxLength(80).IsRequired();
            builder.Property(d => d.DonorId).HasMaxLength(120).IsRequired();
            builder.Property(d => d.DonorEmail).HasMaxLength(180).IsRequired();
            builder.Property(d => d.Amount).HasPrecision(18, 2);
            builder.Property(d => d.PaymentMethod).HasMaxLength(32).IsRequired();
            builder.Property(d => d.Status).HasMaxLength(40).IsRequired();
            builder.HasOne(d => d.Campaign)
                .WithMany(c => c.Donations)
                .HasForeignKey(d => d.CampaignId);
        });

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }

    public static async Task SeedDemoDataAsync(CampaignsDbContext db, CancellationToken cancellationToken = default)
    {
        await db.Database.EnsureCreatedAsync(cancellationToken);

        if (await db.Campaigns.AnyAsync(cancellationToken))
        {
            return;
        }

        db.Campaigns.AddRange(
            new Campaign
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000001"),
                TenantId = "esperanca-solidaria",
                Title = "Biblioteca Viva",
                Description = "Livros, tablets e oficinas de leitura para criancas em contraturno escolar.",
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5)),
                EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(2)),
                GoalAmount = 25000,
                TotalRaised = 8700,
                Status = CampaignStatus.Ativa
            },
            new Campaign
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000002"),
                TenantId = "esperanca-solidaria",
                Title = "Inverno Sem Frio",
                Description = "Kits de agasalho, cobertores e alimentos para familias acompanhadas pela ONG.",
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
                EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)),
                GoalAmount = 18000,
                TotalRaised = 14320,
                Status = CampaignStatus.Ativa
            },
            new Campaign
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000003"),
                TenantId = "esperanca-solidaria",
                Title = "Laboratorio de Futuros",
                Description = "Curso introdutorio de tecnologia, logica e cidadania digital para jovens.",
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-20)),
                EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(4)),
                GoalAmount = 42000,
                TotalRaised = 21450,
                Status = CampaignStatus.Ativa
            });

        await db.SaveChangesAsync(cancellationToken);
    }
}
