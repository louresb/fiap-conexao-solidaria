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
    public DbSet<DonationProjection> DonationProjections => Set<DonationProjection>();

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

        modelBuilder.Entity<DonationProjection>(builder =>
        {
            builder.ToTable("donation_projections");
            builder.HasKey(d => d.DonationId);
            builder.Property(d => d.TenantId).HasMaxLength(80).IsRequired();
            builder.Property(d => d.Amount).HasPrecision(18, 2);
            builder.HasIndex(d => new { d.TenantId, d.ProcessedAtUtc });
            builder.HasIndex(d => new { d.TenantId, d.CampaignId });
        });

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }

    public static async Task SeedDemoDataAsync(CampaignsDbContext db, CancellationToken cancellationToken = default)
    {
        await db.Database.MigrateAsync(cancellationToken);

        if (await db.Campaigns.AnyAsync(cancellationToken))
        {
            await SeedDonationProjectionsAsync(db, cancellationToken);
            return;
        }

        db.Campaigns.AddRange(
            new Campaign
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000001"),
                TenantId = "esperanca-solidaria",
                Title = "Mesa Cheia nas Férias",
                Description = "Kits alimentares e acompanhamento nutricional para famílias durante o recesso escolar.",
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-18)),
                EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(2)),
                GoalAmount = 180000,
                TotalRaised = 46500,
                Status = CampaignStatus.Ativa
            },
            new Campaign
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000002"),
                TenantId = "esperanca-solidaria",
                Title = "Cozinha Parceira",
                Description = "Refeições comunitárias com rastreio de insumos, custo por refeição e prestação de contas.",
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-35)),
                EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3)),
                GoalAmount = 90000,
                TotalRaised = 61200,
                Status = CampaignStatus.Ativa
            },
            new Campaign
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000003"),
                TenantId = "esperanca-solidaria",
                Title = "Conexão para Aprender",
                Description = "Conectividade, equipamentos compartilhados e oficinas digitais para estudantes acompanhados pela ONG.",
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-12)),
                EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(4)),
                GoalAmount = 250000,
                TotalRaised = 72300,
                Status = CampaignStatus.Ativa
            },
            new Campaign
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000004"),
                TenantId = "mare-limpa",
                Title = "Mangue Vivo",
                Description = "Recuperação de áreas de mangue com mutirões, monitoramento e educação ambiental comunitária.",
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-28)),
                EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3)),
                GoalAmount = 140000,
                TotalRaised = 68400,
                Status = CampaignStatus.Ativa
            },
            new Campaign
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000005"),
                TenantId = "mare-limpa",
                Title = "Praia Limpa, Bairro Vivo",
                Description = "Mutirões costeiros mensuráveis com triagem de resíduos e participação de escolas parceiras.",
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-21)),
                EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(2)),
                GoalAmount = 95000,
                TotalRaised = 38200,
                Status = CampaignStatus.Ativa
            },
            new Campaign
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000006"),
                TenantId = "mare-limpa",
                Title = "Escola Azul",
                Description = "Oficinas sobre oceanos, consumo responsável e biodiversidade para estudantes do litoral pernambucano.",
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-9)),
                EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(5)),
                GoalAmount = 60000,
                TotalRaised = 21400,
                Status = CampaignStatus.Ativa
            },
            new Campaign
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000007"),
                TenantId = "futuro-em-rede",
                Title = "Laboratório Aberto",
                Description = "Laboratório maker e trilhas práticas de tecnologia com mentoria para jovens da comunidade.",
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-40)),
                EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(5)),
                GoalAmount = 180000,
                TotalRaised = 84600,
                Status = CampaignStatus.Ativa
            },
            new Campaign
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000008"),
                TenantId = "futuro-em-rede",
                Title = "Bolsa Dados para Estudar",
                Description = "Conectividade e suporte técnico para estudantes permanecerem em trilhas de formação profissional.",
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-16)),
                EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3)),
                GoalAmount = 75000,
                TotalRaised = 29750,
                Status = CampaignStatus.Ativa
            },
            new Campaign
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000009"),
                TenantId = "futuro-em-rede",
                Title = "Primeiro Código",
                Description = "Formação introdutória em lógica, programação e cidadania digital para novos talentos.",
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-8)),
                EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(4)),
                GoalAmount = 110000,
                TotalRaised = 41600,
                Status = CampaignStatus.Ativa
            });

        await db.SaveChangesAsync(cancellationToken);
        await SeedDonationProjectionsAsync(db, cancellationToken);
    }

    private static async Task SeedDonationProjectionsAsync(
        CampaignsDbContext db,
        CancellationToken cancellationToken)
    {
        if (await db.DonationProjections.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        db.DonationProjections.AddRange(
            new DonationProjection
            {
                DonationId = Guid.Parse("20000000-0000-0000-0000-000000000001"),
                CampaignId = Guid.Parse("10000000-0000-0000-0000-000000000001"),
                TenantId = "esperanca-solidaria",
                Amount = 250,
                ProcessedAtUtc = now.AddHours(-2)
            },
            new DonationProjection
            {
                DonationId = Guid.Parse("20000000-0000-0000-0000-000000000002"),
                CampaignId = Guid.Parse("10000000-0000-0000-0000-000000000002"),
                TenantId = "esperanca-solidaria",
                Amount = 120,
                ProcessedAtUtc = now.AddHours(-7)
            },
            new DonationProjection
            {
                DonationId = Guid.Parse("20000000-0000-0000-0000-000000000003"),
                CampaignId = Guid.Parse("10000000-0000-0000-0000-000000000004"),
                TenantId = "mare-limpa",
                Amount = 340,
                ProcessedAtUtc = now.AddHours(-3)
            },
            new DonationProjection
            {
                DonationId = Guid.Parse("20000000-0000-0000-0000-000000000004"),
                CampaignId = Guid.Parse("10000000-0000-0000-0000-000000000005"),
                TenantId = "mare-limpa",
                Amount = 95,
                ProcessedAtUtc = now.AddHours(-10)
            },
            new DonationProjection
            {
                DonationId = Guid.Parse("20000000-0000-0000-0000-000000000005"),
                CampaignId = Guid.Parse("10000000-0000-0000-0000-000000000007"),
                TenantId = "futuro-em-rede",
                Amount = 500,
                ProcessedAtUtc = now.AddHours(-4)
            },
            new DonationProjection
            {
                DonationId = Guid.Parse("20000000-0000-0000-0000-000000000006"),
                CampaignId = Guid.Parse("10000000-0000-0000-0000-000000000009"),
                TenantId = "futuro-em-rede",
                Amount = 180,
                ProcessedAtUtc = now.AddHours(-11)
            });

        await db.SaveChangesAsync(cancellationToken);
    }
}
