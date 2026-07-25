using Prometheus;

namespace ConexaoSolidaria.Donations.Worker.Observability;

public static class DonationMetrics
{
    public static readonly Counter Processed = Metrics.CreateCounter(
        "conexao_donations_processed_total",
        "Total de doações processadas após confirmação de pagamento.",
        new CounterConfiguration { LabelNames = ["tenant", "status"] });

    public static readonly Histogram ProcessingLatency = Metrics.CreateHistogram(
        "conexao_donation_processing_seconds",
        "Tempo entre a confirmação do pagamento e o processamento da doação.",
        new HistogramConfiguration
        {
            LabelNames = ["tenant"],
            Buckets = Histogram.ExponentialBuckets(0.05, 2, 10)
        });
}