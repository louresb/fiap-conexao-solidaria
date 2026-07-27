# Modelo de Segurança

## Identidade e autorização

Keycloak é a autoridade OIDC. O Web App usa Authorization Code com PKCE e o Gateway valida JWT por assinatura, issuer e audience. As APIs aplicam roles `GestorONG` e `Doador`; `tenant_id` vem do token autenticado e não é aceito do corpo como fonte de autoridade.

## Isolamento de tenant

O `TenantId` participa das consultas transacionais, chaves de cache, documentos de busca, eventos, auditoria, logs, métricas e traces. Os testes automatizados rejeitam acesso cross-tenant. O modelo atual é shared application/shared database com isolamento lógico; isolamento físico por tenant é uma evolução compatível.

## Segredos

O ambiente local gera `.env` ignorado pelo Git. AKS usa Azure Key Vault CSI; EKS usa AWS Secrets Manager, ASCP e Pod Identity. Os pipelines acessam Azure e AWS por OIDC com tokens curtos, sem credenciais permanentes.

## Dados e eventos

PostgreSQL preserva transações e Outbox/Inbox. Webhooks exigem assinatura e idempotência. A auditoria é append-only em MongoDB e aplica redação de campos sensíveis antes de persistir payloads. Correlation ID e trace context conectam HTTP, mensagens e registros operacionais.

## Runtime e cadeia de suprimentos

Containers executam como não-root, sem privilege escalation, com filesystem somente leitura, capabilities removidas, seccomp, requests/limits e probes. O pipeline executa Trivy em configuração e imagens, gera SBOM/provenance e publica em registries privados. Dependabot monitora NuGet, GitHub Actions, Terraform e todos os Dockerfiles.

## Riscos residuais

- Os bancos e brokers no cluster usam topologia de demonstração, sem HA de dados entre zonas.
- O pagamento é sandbox; provedores reais exigem gestão de disputa, conciliação e requisitos contratuais.
- O hostname `sslip.io` é adequado à validação técnica; produção deve usar domínio controlado e política DNS própria.
- Multi-cloud significa portabilidade e dois ambientes independentes, não consistência distribuída active-active.
