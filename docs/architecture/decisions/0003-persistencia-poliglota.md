# ADR 0003: Persistência poliglota

- Status: aceita
- Data: 2026-07-26

## Contexto

Transações, cache, busca textual, auditoria e documentos institucionais possuem garantias e padrões de acesso diferentes.

## Decisão

- PostgreSQL: estado transacional, ledger, Outbox e Inbox, com banco por capacidade.
- Redis: cache de campanhas e transparência, invalidado por evento.
- OpenSearch: índice de campanhas e busca fuzzy.
- MongoDB: auditoria append-only por tenant e correlação.
- Corpus Markdown versionado: fontes verificáveis usadas pelo serviço de conhecimento.

## Consequências

- Cada tecnologia possui responsabilidade verificável e não é adotada apenas como demonstração.
- A plataforma assume custo operacional de múltiplos motores.
- Migrações EF Core versionam os bancos relacionais; índices e cache são reconstruíveis.
