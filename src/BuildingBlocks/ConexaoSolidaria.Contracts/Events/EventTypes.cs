namespace ConexaoSolidaria.Contracts.Events;

public static class EventTypes
{
    public const string DonorRegistered = "DonorRegistered";
    public const string CampaignCreated = "CampaignCreated";
    public const string CampaignUpdated = "CampaignUpdated";
    public const string DonationIntentCreated = "DonationIntentCreated";
    public const string PaymentAwaitingConfirmation = "PaymentAwaitingConfirmation";
    public const string PaymentConfirmed = "PaymentConfirmed";
    public const string DonationProcessed = "DonationProcessed";
    public const string CampaignGoalReached = "CampaignGoalReached";
    public const string KnowledgeQuestionAnswered = "KnowledgeQuestionAnswered";
    public const string AuditRecorded = "AuditRecorded";
}