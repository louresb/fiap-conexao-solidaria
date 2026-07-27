# ADR 0007: Clean Architecture e CQRS pragmático

- Status: aceita
- Data: 2026-07-26

## Contexto

Os contextos possuem ritmos diferentes, mas não justificam camadas e mediadores adicionados apenas por simetria. O fluxo de doação exige regras, integração externa e resultado explícito; consultas públicas exigem modelos otimizados por cache e busca.

## Decisão

Organizar cada bounded context por capacidade e caso de uso. Commands validam invariantes, persistem pelo banco proprietário e publicam eventos pelo Outbox. Queries não alteram estado e podem usar projeções em Redis ou OpenSearch. O contrato HTTP é separado do envelope de integração.

Handlers são classes explícitas quando o caso de uso possui ramificações ou dependências relevantes, como `CreateDonationHandler`. Endpoints simples podem permanecer como funções locais do contexto. Não será introduzido um mediator ou uma hierarquia genérica de `Application/Domain/Infrastructure` sem complexidade que justifique a indireção.

## Consequências

- CQRS representa separação de responsabilidade e modelo de leitura, não duplicação obrigatória de bancos ou projetos.
- Regras de domínio permanecem testáveis sem broker ou servidor HTTP.
- Composition roots podem ser fracionados em vertical slices à medida que cada capacidade crescer.
- Testes arquiteturais impedem referências diretas entre bounded contexts.
