# ADR 0004: Identidade e multitenancy

- Status: aceita
- Data: 2026-07-26

## Contexto

Doadores e gestores possuem permissões distintas, e uma mesma plataforma atende organizações com marca e dados próprios.

## Decisão

Usar Keycloak como provedor OpenID Connect. Tokens carregam `sub`, `email`, roles e `tenant_id`. APIs validam assinatura, audience, role e tenant; o tenant autenticado prevalece sobre valores enviados pelo cliente. `TenantId` também compõe chaves, consultas, eventos, logs, métricas e auditoria.

O modelo inicial é shared application/shared database com discriminação obrigatória por tenant. Não há impersonação cross-tenant nem escolha livre de tenant após autenticação.

## Consequências

- Senhas e fluxos de autenticação não são implementados pelas APIs de domínio.
- Todo acesso a dados precisa provar o filtro de tenant.
- Isolamento físico por tenant pode ser introduzido para clientes regulados sem alterar contratos externos.
