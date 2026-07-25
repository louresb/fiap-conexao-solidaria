using System.Text.Json;

using ConexaoSolidaria.Contracts.Events;
using ConexaoSolidaria.Contracts.Knowledge;
using ConexaoSolidaria.Infrastructure.Http;
using ConexaoSolidaria.Infrastructure.OpenApi;
using ConexaoSolidaria.Knowledge.Api.Retrieval;
using ConexaoSolidaria.Knowledge.Api.Services;

using MassTransit;

using Prometheus;

using Scalar.AspNetCore;

using Serilog;
using Serilog.Events;
using Serilog.Sinks.Grafana.Loki;

var builder = WebApplication.CreateBuilder(args);
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

builder.Host.UseSerilog((context, configuration) =>
{
    configuration
        .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
        .Enrich.FromLogContext()
        .WriteTo.Console();

    var lokiUrl = context.Configuration["Observability:LokiUrl"];
    if (!string.IsNullOrWhiteSpace(lokiUrl))
    {
        configuration.WriteTo.GrafanaLoki(lokiUrl);
    }
});

builder.Services.AddSingleton<IKnowledgeRetriever, KnowledgeRetriever>();
builder.Services.AddSingleton<KnowledgeAnswerService>();
builder.Services.AddMassTransit(bus =>
{
    bus.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(
            builder.Configuration["RabbitMq:Host"] ?? "localhost",
            builder.Configuration["RabbitMq:VirtualHost"] ?? "/",
            host =>
            {
                host.Username(builder.Configuration["RabbitMq:Username"] ?? "guest");
                host.Password(builder.Configuration["RabbitMq:Password"] ?? string.Empty);
            });
    });
});
builder.Services.AddConexaoSolidariaOpenApi(
    "Conexao Solidaria - Knowledge API",
    "Respostas baseadas em fontes institucionais verificaveis e rastreaveis.");

var app = builder.Build();

app.UseCorrelationAndTenant();
app.UseHttpMetrics();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy", service = "knowledge-api" }))
    .WithTags("Operacao")
    .WithName("KnowledgeLiveness")
    .WithSummary("Verifica se a Knowledge API esta em execucao.");
app.MapMetrics();
app.MapGet("/health/ready", (
    IKnowledgeRetriever retriever,
    HttpContext http) =>
{
    var documentCount = retriever.CountDocuments(http.TenantId());
    return documentCount > 0
        ? Results.Ok(new { status = "Healthy", documents = documentCount })
        : Results.Json(new { status = "Degraded", documents = 0 }, statusCode: StatusCodes.Status503ServiceUnavailable);
})
    .WithTags("Operacao")
    .WithName("KnowledgeReadiness")
    .WithSummary("Verifica se a base institucional possui fontes carregadas.");

app.MapPost("/api/knowledge/ask", async (
    AskKnowledgeRequest request,
    KnowledgeAnswerService service,
    IPublishEndpoint publishEndpoint,
    HttpContext http,
    CancellationToken cancellationToken) =>
{
    var question = request.Question.Trim();
    if (question.Length is < 5 or > 500)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["question"] = ["A pergunta deve ter entre 5 e 500 caracteres."]
        });
    }

    var tenantId = http.TenantId();
    var correlationId = http.CorrelationId();
    var answer = service.Answer(tenantId, question, correlationId);
    var auditPayload = new KnowledgeQuestionAnsweredPayload(
        question,
        answer.Answered,
        answer.Sources.Select(source => source.DocumentId).ToList(),
        tenantId);

    await publishEndpoint.Publish(
        IntegrationEvent.Create(
            EventTypes.KnowledgeQuestionAnswered,
            tenantId,
            correlationId,
            "knowledge-api",
            JsonSerializer.Serialize(auditPayload, jsonOptions)),
        cancellationToken);

    return Results.Ok(answer);
})
    .WithTags("Conhecimento")
    .WithName("AskInstitutionalKnowledge")
    .WithSummary("Responde uma pergunta com fontes institucionais rastreaveis.");

app.Run();

public partial class Program;