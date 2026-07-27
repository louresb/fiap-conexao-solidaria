# ADR 0002: RabbitMQ, Outbox e Inbox

- Status: aceita
- Data: 2026-07-26

## Contexto

Pagamento, atualização da arrecadação, auditoria e projeções não devem formar uma transação distribuída síncrona. Perda ou duplicação de mensagens comprometeria o ledger e a transparência pública.

## Decisão

Usar RabbitMQ e MassTransit com Outbox transacional no PostgreSQL dos produtores e Inbox nos consumidores. Eventos usam envelope versionado com `eventId`, `eventType`, `tenantId`, `correlationId`, `causationId`, `occurredAtUtc`, `source` e `payload`.

O `Donations.Api` registra a intenção; `Payments.Api` prepara e confirma o pagamento; `Donations.Api` consolida o ledger; `Campaigns.Worker` atualiza a projeção arrecadada e invalida cache/índice. Consumidores são idempotentes e filas possuem retry e dead-letter.

## Consequências

- O fluxo aceita consistência eventual explícita e observável.
- Publicação e persistência de negócio permanecem atômicas.
- Operação precisa monitorar idade da fila, retries e DLQ.
