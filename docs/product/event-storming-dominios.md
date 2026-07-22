# Event storming e dominios

## Bounded contexts candidatos

| Contexto | Responsabilidade |
|---|---|
| Identity | Usuarios, perfis, roles, tenant e login. |
| Tenancy | Configuracao de tenant, tema, modulos e limites. |
| Campaigns | Ciclo de vida de campanhas, metas, status e transparencia. |
| Donations | Intencao, confirmacao, recorrencia e recibos. |
| Payments | Cobranca Pix, gateway, webhook, idempotencia e conciliacao. |
| Transparency | Prestacao de contas, evidencias e relatorios publicos. |
| Audit | Eventos, trilha append-only e exportacao. |
| Notifications | Comunicacao com doadores, gestores e parceiros. |
| Intelligence | RAG, copiloto, recomendacoes e guardrails. |

## Eventos de dominio iniciais

- TenantCreated
- TenantThemeUpdated
- UserRegistered
- RoleAssigned
- DonorRegistered
- CampaignDraftCreated
- CampaignSubmittedForApproval
- CampaignApproved
- CampaignPublished
- CampaignUpdated
- DonationIntentCreated
- PaymentIntentCreated
- PixChargeCreated
- PixQrCodeGenerated
- PaymentWebhookReceived
- PaymentConfirmed
- PaymentExpired
- PaymentFailed
- DonationConfirmed
- DonationProcessed
- CampaignGoalReached
- EvidenceUploaded
- AccountabilityReportPublished
- AuditEntryRecorded
- NotificationRequested
- NotificationSent
- AiAnswerGenerated
- AiRecommendationApproved

## Politicas

- Ao confirmar doacao, publicar evento para atualizar total da campanha.
- Ao gerar cobranca Pix, persistir QR Code/copia-e-cola e data de expiracao.
- Ao receber webhook de pagamento, validar assinatura e idempotencia antes de confirmar doacao.
- Ao atualizar total, invalidar cache da pagina publica.
- Ao publicar campanha, indexar conteudo para busca.
- Ao criar/editar campanha, registrar auditoria.
- Ao publicar prestacao de contas, notificar doadores vinculados.
- Ao responder com IA, persistir prompt, resposta, fontes e tenant.
- Ao trocar tenant, aplicar tema e restringir dados por `tenantId`.
