# Taxonomia documental

## Tipos de documento

| Tipo | Exemplo | Uso no RAG |
|---|---|---|
| policy | Politica de uso de doacoes | Respostas normativas. |
| faq | FAQ de doadores | Atendimento publico. |
| guide | Guia de criacao de campanhas | Apoio operacional. |
| report | Relatorio trimestral | Prestacao de contas. |
| contract_template | Termos de parceria | Parcerias empresariais. |
| audit_manual | Manual de prestacao de contas | Auditoria e compliance. |
| brand_guide | Guia de comunicacao | Tom e estilo. |

## Classificacao

- public: pode aparecer para visitantes.
- donor: exige login de doador.
- internal: apenas equipe da ONG.
- restricted: financeiro, compliance, auditoria ou admin.
- platform: administracao SaaS.

## Metadados de indexacao

- `tenantId`
- `documentType`
- `classification`
- `version`
- `effectiveDate`
- `reviewDate`
- `tags`
- `allowedRoles`
- `sourcePath`

