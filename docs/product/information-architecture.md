# Arquitetura da informacao

## Objetivo

Definir as paginas antes de desenhar interface. A experiencia precisa parecer produto real para ONG, doador e administrador, com atalhos de avaliador discretos.

## Areas publicas

| Pagina | Objetivo | Conteudo principal |
|---|---|---|
| Home publica do tenant | Apresentar ONG, impacto e campanhas. | Hero, CTA, indicadores, campanhas em destaque, transparencia. |
| Lista de campanhas | Explorar campanhas ativas. | Filtros, categorias, progresso, urgencia, transparencia. |
| Detalhe da campanha | Converter intencao em doacao. | Historia, meta, itens, timeline, evidencias, CTA. |
| Checkout Pix | Concluir doacao com minimo atrito. | Valor, QR Code, copia-e-cola, expiracao, status. |
| Confirmacao da doacao | Reforcar confianca e proximo passo. | Recibo, protocolo, campanha, link de acompanhamento. |
| Transparencia publica | Mostrar uso de recursos. | Relatorios, evidencias, indicadores e politicas. |
| FAQ | Reduzir duvidas e abandono. | Perguntas sobre doacao, dados, recorrencia e prestacao. |

## Area do doador

| Pagina | Objetivo | Conteudo principal |
|---|---|---|
| Login | Entrar como doador/gestor/admin sem confusao. | OIDC, senha, opcoes de demo discretas. |
| Meu impacto | Mostrar historico e valor emocional. | Doacoes, campanhas apoiadas, recibos, impacto acumulado. |
| Minhas doacoes | Controle financeiro. | Status, metodo, recibo, recorrencia, cancelamento. |
| Campanhas acompanhadas | Retencao. | Atualizacoes, evidencias e recomendacoes. |
| Preferencias | LGPD e comunicacao. | Canais, frequencia, consentimentos. |

## Area do gestor

| Pagina | Objetivo | Conteudo principal |
|---|---|---|
| Overview | Tomada de decisao rapida. | Captacao, risco, pendencias, alertas, atividades. |
| Campanhas | Gestao do ciclo de vida. | Drafts, aprovacao, publicacao, indicadores. |
| Doacoes | Acompanhamento financeiro. | Confirmadas, aguardando Pix, falhas, conciliacao. |
| Transparencia | Prestacao de contas. | Evidencias, relatorios, documentos, marcos. |
| Doadores | Relacionamento. | Segmentos, recorrentes, churn, comunicacao. |
| Comunicacao | Campanhas de mensagem. | Templates, envios, aberturas, in-app. |
| IA Assistida | Copiloto com fontes. | Perguntas, respostas, fontes, recomendacoes. |
| Auditoria | Rastreabilidade. | Eventos, atores, antes/depois, exportacao. |

## Area do administrador SaaS

| Pagina | Objetivo | Conteudo principal |
|---|---|---|
| Tenants | Operar multi-tenancy. | Lista, status, plano, tema, modulos. |
| Branding | Configurar experiencia por tenant. | Cores, logos, textos, imagens e vocabulario. |
| Usuarios e roles | Governanca. | Usuarios, roles, convites, bloqueios. |
| Integracoes | Gateways, e-mail, IA e webhooks. | Status, secrets externos, health. |
| Observabilidade | Operacao. | Health checks, logs, metricas e filas. |
| Feature flags | Controle de rollout. | Funcionalidades por tenant. |

## Modo avaliador

O modo avaliador nao deve poluir o produto. Ele deve aparecer como:

- botao discreto "Abrir demo guiada";
- modal com perfis de acesso;
- acoes de teste: entrar como gestor, entrar como doador, simular Pix, abrir observabilidade;
- sempre com rotulo "ambiente de demonstracao".

## Priorizacao de desenho

1. Home publica do tenant.
2. Detalhe da campanha.
3. Checkout Pix.
4. Confirmacao.
5. Dashboard gestor.
6. IA assistida.
7. Area do doador.
8. Admin SaaS/tenants.

