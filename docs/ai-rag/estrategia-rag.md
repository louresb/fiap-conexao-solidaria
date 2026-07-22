# Estrategia RAG

## Objetivo

O modulo de IA deve funcionar como copiloto de transparencia e operacao. Ele nao deve substituir decisao humana nem inventar politica institucional.

## Casos de uso prioritarios

1. Responder perguntas sobre politicas da ONG com fontes.
2. Resumir campanha para gestor, doador e parceiro.
3. Explicar variacao de arrecadacao com base em eventos e indicadores.
4. Sugerir acoes de engajamento para campanha em risco.
5. Gerar rascunho de relatorio de impacto.

## Corpus inicial

- Estatuto resumido.
- Politica de transparencia.
- Politica de uso de doacoes.
- Guia de criacao de campanhas.
- FAQ de doadores.
- Manual de prestacao de contas.
- Relatorio trimestral ficticio.
- Termos de parceria com empresas.
- Guia de comunicacao com doadores.
- Politica LGPD.
- Eventos e auditoria, quando autorizados pelo perfil.

## Politica de resposta

Uma resposta de IA deve citar documentos usados, indicar quando nao encontrou fonte suficiente, separar fato institucional de recomendacao, respeitar role e tenant, evitar dados pessoais desnecessarios e registrar prompt, resposta, fontes e correlationId.

## Decisoes sensiveis bloqueadas

A IA nao pode decidir automaticamente elegibilidade de beneficiario, prioridade de atendimento individual, cancelamento de campanha, destino final de saldo residual, sancao de usuario ou conclusao de auditoria.

