# Guardrails de IA

## Principios

- Fonte antes de fluencia.
- Transparencia antes de persuasao.
- Humano no controle para decisoes sensiveis.
- Minimizacao de dados pessoais.
- Isolamento rigido por tenant.

## Regras obrigatorias

1. Nunca responder sobre dados de outro tenant.
2. Nunca expor dados pessoais de beneficiarios em resposta publica.
3. Nunca afirmar uso de doacao sem evidencia ou status transacional.
4. Sempre sinalizar quando uma resposta e recomendacao.
5. Sempre citar fontes em perguntas institucionais.
6. Registrar prompt e resposta para auditoria quando o usuario estiver autenticado.
7. Bloquear automacao sensivel sem aprovacao humana.

## Riscos

| Risco | Mitigacao |
|---|---|
| Alucinacao institucional | Responder apenas com fonte ou fallback. |
| Vazamento entre tenants | Filtro obrigatorio por tenant e ACL. |
| Exposicao de criancas/adolescentes | Pseudonimizacao e bloqueio em respostas publicas. |
| Manipulacao emocional | Templates revisados e tom de voz digno. |
| Decisao automatica indevida | Human-in-the-loop e trilha de aprovacao. |
| Prompt injection em documentos | Sanitizacao, classificacao e revisao de corpus. |

