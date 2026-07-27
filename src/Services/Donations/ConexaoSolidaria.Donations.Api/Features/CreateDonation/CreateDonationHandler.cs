using System.Text.Json;

using ConexaoSolidaria.Contracts.Donations;
using ConexaoSolidaria.Contracts.Events;
using ConexaoSolidaria.Contracts.Validation;
using ConexaoSolidaria.Donations.Api.Campaigns;
using ConexaoSolidaria.Donations.Api.Data;

using MassTransit;

namespace ConexaoSolidaria.Donations.Api.Features.CreateDonation;

public sealed record CreateDonationCommand(
    CreateDonationRequest Request,
    string TenantId,
    string CorrelationId,
    string? AuthenticatedDonorId,
    string? AuthenticatedDonorEmail);

public enum CreateDonationStatus
{
    Created,
    Invalid,
    CampaignNotFound,
    CampaignUnavailable
}

public sealed record CreateDonationResult(
    CreateDonationStatus Status,
    Donation? Donation = null,
    string? Error = null,
    Dictionary<string, string[]>? ValidationErrors = null);

public sealed class CreateDonationHandler(
    DonationsDbContext db,
    ICampaignEligibilityGateway campaigns,
    IPublishEndpoint publishEndpoint)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CreateDonationResult> HandleAsync(
        CreateDonationCommand command,
        CancellationToken cancellationToken)
    {
        var request = command.Request;
        var errors = DonationRules.Validate(request.Amount, request.PaymentMethod);
        if (errors.Count > 0)
        {
            return new CreateDonationResult(CreateDonationStatus.Invalid, ValidationErrors: errors);
        }

        var campaign = await campaigns.GetAsync(request.CampaignId, command.TenantId, cancellationToken);
        if (campaign is null)
        {
            return new CreateDonationResult(CreateDonationStatus.CampaignNotFound, Error: "Campanha nao encontrada.");
        }

        if (!CampaignRules.CanReceiveDonation(campaign.Status, campaign.StartDate, campaign.EndDate))
        {
            return new CreateDonationResult(
                CreateDonationStatus.CampaignUnavailable,
                Error: "A campanha nao esta disponivel para doacoes.");
        }

        var donation = new Donation
        {
            CampaignId = campaign.Id,
            CampaignTitle = campaign.Title,
            TenantId = command.TenantId,
            DonorId = command.AuthenticatedDonorId ?? request.DonorId ?? "demo-donor",
            DonorEmail = command.AuthenticatedDonorEmail ?? request.DonorEmail ?? "doador@demo.org",
            Amount = request.Amount,
            PaymentMethod = request.PaymentMethod.Trim().ToLowerInvariant()
        };
        db.Donations.Add(donation);

        var payload = new DonationIntentCreatedPayload(
            donation.Id,
            donation.CampaignId,
            donation.DonorId,
            donation.DonorEmail,
            donation.Amount,
            donation.TenantId,
            donation.PaymentMethod);
        await publishEndpoint.Publish(
            IntegrationEvent.Create(
                EventTypes.DonationIntentCreated,
                donation.TenantId,
                command.CorrelationId,
                "donations-api",
                JsonSerializer.Serialize(payload, JsonOptions)),
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateDonationResult(CreateDonationStatus.Created, donation);
    }
}
