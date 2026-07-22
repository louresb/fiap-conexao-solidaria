# Contexto operacional

> Documento de produto para orientar dominio, fluxos, dados de demo e requisitos. A ONG e ficticia.

## Problemas atendidos

- Inseguranca alimentar em familias vulneraveis.
- Risco de evasao e baixa permanencia escolar.
- Falta de conectividade significativa para estudo.
- Sobrecarga de cuidadoras.
- Resposta fragil a emergencias locais, especialmente chuvas.
- Baixa confianca publica na forma como doacoes sao usadas.

## Modelo operacional

Cada campanha deve ter objetivo social, meta financeira, regiao atendida, categoria de impacto, publico beneficiado, plano de itens financiados, centro de custo, marcos de prestacao de contas, evidencias esperadas, responsavel interno, status de aprovacao e regras de encerramento.

## Jornada operacional

| Etapa | Acao principal | Evidencia gerada |
|---|---|---|
| Diagnostico | Levantamento territorial e justificativa social. | Brief da campanha, metas, regiao e indicadores. |
| Aprovacao | Validacao por gestor, financeiro e compliance. | Registro de aprovacao e orcamento. |
| Publicacao | Exposicao da campanha ao publico. | Pagina publica, card, tags e metas. |
| Captacao | Recebimento e conciliacao de doacoes. | Transacoes, recibos e CRM de doadores. |
| Execucao | Compra, entrega, oficinas, mutiroes ou servicos. | Notas, fotos, checklists e listas de entrega. |
| Monitoramento | Acompanhamento financeiro e social. | Dashboards, alertas e risco de meta. |
| Prestacao de contas | Consolidacao publica e gerencial. | Relatorio final, anexos e log de auditoria. |
| Aprendizado | Retrospectiva e reuso de conhecimento. | Licoes aprendidas, playbooks, FAQ e documentos RAG. |

## Regras de uso das doacoes

| Tipo de recurso | Regra operacional |
|---|---|
| Doacao para campanha especifica | Vinculada ao orcamento da campanha; saldo residual segue regra publicada antes da doacao. |
| Doacao institucional livre | Distribuida entre programas, operacao, tecnologia e reserva, conforme politica anual. |
| Doacao recorrente | Prioriza despesas previsiveis: alimentacao, conectividade, equipe minima e comunicacao. |
| Apoio empresarial | Pode ser financeiro, produto, servico ou matchfunding; exige termo de parceria. |
| Apoio emergencial | Fluxo de aprovacao abreviado, com transparencia reforcada e prestacao frequente. |

## Camadas de transparencia

- Publica: meta, arrecadacao, itens financiados, regioes atendidas, documentos resumidos e evidencias autorizadas.
- Gerencial: orcamento por centro de custo, recibos, variacao entre previsto e executado, pendencias e risco.
- Auditoria: ator, data/hora, entidade afetada, estado anterior/posterior, correlationId e trilha append-only.

## Requisitos derivados

- Toda entidade de dominio deve carregar `tenantId`.
- Toda acao critica deve gerar evento auditavel.
- Toda campanha publica deve ter uma pagina de transparencia.
- Toda doacao deve ter status rastreavel.
- Toda recomendacao de IA deve citar fonte ou deixar claro que e sugestao.
- Toda informacao de beneficiarios deve respeitar minimizacao de dados.
- O sistema deve permitir branding e vocabulos diferentes por ONG.

