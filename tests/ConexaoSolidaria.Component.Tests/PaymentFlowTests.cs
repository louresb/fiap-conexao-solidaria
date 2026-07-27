using System.Text.Json;

using ConexaoSolidaria.Contracts.Events;
using ConexaoSolidaria.Payments.Api.Consumers;
using ConexaoSolidaria.Payments.Api.Data;
using ConexaoSolidaria.Payments.Api.Providers;
using ConexaoSolidaria.Payments.Api.Services;

using MassTransit.Testing;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ConexaoSolidaria.Component.Tests;

public sealed class PaymentFlowTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Donation_intent_creates_one_payment_and_preserves_event_context()
    {
        await using var host = await CreateHostAsync();
        var consumer = host.Harness.GetConsumerHarness<DonationIntentCreatedConsumer>();
        var payload = CreateDonationIntent();
        var correlationId = $"corr-{Guid.NewGuid():N}";
        var message = CreateEvent(EventTypes.DonationIntentCreated, payload.TenantId, correlationId, payload);

        await host.Harness.Bus.Publish(message);

        Assert.True(await consumer.Consumed.Any<IntegrationEvent>(x => x.Context.Message.EventId == message.EventId));

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var payment = await db.Payments.SingleAsync();
        var published = host.Harness.Published.Select<IntegrationEvent>()
            .Single(x => x.Context.Message.EventType == EventTypes.PaymentAwaitingConfirmation)
            .Context.Message;

        Assert.Equal(payload.DonationId, payment.DonationId);
        Assert.Equal(payload.TenantId, payment.TenantId);
        Assert.Equal(payload.PaymentMethod, payment.PaymentMethod);
        Assert.Equal(correlationId, published.CorrelationId);
        Assert.Equal(message.EventId.ToString(), published.CausationId);
        Assert.Equal(payload.TenantId, published.TenantId);
    }

    [Fact]
    public async Task Repeated_donation_intent_does_not_duplicate_payment()
    {
        await using var host = await CreateHostAsync();
        var consumer = host.Harness.GetConsumerHarness<DonationIntentCreatedConsumer>();
        var payload = CreateDonationIntent();
        var first = CreateEvent(EventTypes.DonationIntentCreated, payload.TenantId, "corr-first", payload);
        var repeated = CreateEvent(EventTypes.DonationIntentCreated, payload.TenantId, "corr-repeated", payload);

        await host.Harness.Bus.Publish(first);
        Assert.True(await consumer.Consumed.Any<IntegrationEvent>(x => x.Context.Message.EventId == first.EventId));
        await host.Harness.Bus.Publish(repeated);
        Assert.True(await consumer.Consumed.Any<IntegrationEvent>(x => x.Context.Message.EventId == repeated.EventId));

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();

        Assert.Equal(1, await db.Payments.CountAsync());
        Assert.Single(
            host.Harness.Published.Select<IntegrationEvent>(),
            x => x.Context.Message.EventType == EventTypes.PaymentAwaitingConfirmation);
    }

    [Fact]
    public async Task Payment_confirmation_is_idempotent_and_publishes_once()
    {
        await using var host = await CreateHostAsync();
        Guid paymentId;

        await using (var seedScope = host.Services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
            var payment = CreatePaymentIntent();
            paymentId = payment.Id;
            db.Payments.Add(payment);
            await db.SaveChangesAsync();
        }

        await using (var confirmationScope = host.Services.CreateAsyncScope())
        {
            var db = confirmationScope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
            var service = confirmationScope.ServiceProvider.GetRequiredService<PaymentConfirmationService>();
            var payment = await db.Payments.SingleAsync(x => x.Id == paymentId);

            var first = await service.ConfirmAsync(payment, "provider-event-001", "corr-payment", CancellationToken.None);
            var repeated = await service.ConfirmAsync(payment, "provider-event-001", "corr-payment", CancellationToken.None);

            Assert.Equal(PaymentConfirmationResult.Confirmed, first);
            Assert.Equal(PaymentConfirmationResult.AlreadyProcessed, repeated);
        }

        await using var assertionScope = host.Services.CreateAsyncScope();
        var assertionDb = assertionScope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var confirmed = host.Harness.Published.Select<IntegrationEvent>()
            .Where(x => x.Context.Message.EventType == EventTypes.PaymentConfirmed)
            .ToList();

        Assert.Equal("Confirmed", (await assertionDb.Payments.SingleAsync()).Status);
        Assert.Equal(1, await assertionDb.Webhooks.CountAsync());
        Assert.Single(confirmed);
        Assert.Equal("corr-payment", confirmed[0].Context.Message.CorrelationId);
    }

    private static async Task<MassTransitComponentHost> CreateHostAsync()
    {
        var host = await MassTransitComponentHost.CreateAsync(
            (services, connection) =>
            {
                services.AddDbContext<PaymentsDbContext>(options => options.UseSqlite(connection));
                services.Configure<FakePaymentOptions>(options =>
                {
                    options.ProviderName = "component-sandbox";
                    options.WebhookSecret = "component-test-secret";
                    options.ExpirationMinutes = 15;
                });
                services.AddSingleton<IFakePaymentProvider, FakePaymentProvider>();
                services.AddScoped<PaymentConfirmationService>();
            },
            bus => bus.AddConsumer<DonationIntentCreatedConsumer>());

        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Database.EnsureCreatedAsync();
        return host;
    }

    private static DonationIntentCreatedPayload CreateDonationIntent() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "donor-component",
        "doador@example.org",
        137.40m,
        "esperanca-solidaria",
        "pix");

    private static PaymentIntent CreatePaymentIntent() => new()
    {
        DonationId = Guid.NewGuid(),
        CampaignId = Guid.NewGuid(),
        TenantId = "esperanca-solidaria",
        Amount = 91.75m,
        Provider = "component-sandbox",
        ProviderPaymentId = $"sandbox-{Guid.NewGuid():N}",
        ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(15)
    };

    private static IntegrationEvent CreateEvent<TPayload>(
        string eventType,
        string tenantId,
        string correlationId,
        TPayload payload) => IntegrationEvent.Create(
            eventType,
            tenantId,
            correlationId,
            "component-tests",
            JsonSerializer.Serialize(payload, JsonOptions));
}
