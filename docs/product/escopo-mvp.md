# Escopo MVP

## Incluido no MVP

### Publico

- Landing page institucional por tenant.
- Lista de campanhas ativas.
- Pagina de detalhe da campanha.
- Doacao simulada com status rastreavel.
- Transparencia basica por campanha.

### Doador

- Login.
- Perfil basico.
- Historico de doacoes.
- Recibos simulados.
- Impacto acumulado.
- Campanhas acompanhadas.
- Checkout Pix com QR Code/copia-e-cola em modo sandbox/demo.

### Gestor ONG

- Dashboard operacional.
- CRUD de campanhas.
- Aprovacao/publicacao de campanha.
- Indicadores de captacao e execucao.
- Pendencias de documentacao.
- Timeline de atividade.
- IA assistida com fontes.

### Administrador SaaS

- Lista de tenants.
- Configuracao de tema por tenant.
- Health checks.
- Auditoria de acoes criticas.
- Feature flags simples.

### Pagamentos

- Intencao de doacao.
- Criacao de cobranca Pix.
- QR Code/copia-e-cola.
- Webhook ou simulacao de confirmacao.
- Idempotencia por evento do provedor.
- Auditoria de eventos financeiros.

## Fora do MVP

- Pagamento real.
- Conciliacao bancaria real.
- Nota fiscal/recibo fiscal oficial.
- App mobile nativo.
- Isolamento enterprise com banco dedicado por tenant.
- Active-active multi-cloud real.
- Decisao automatica de elegibilidade social por IA.

## Pagamentos no MVP

O MVP nao deve prometer liquidacao financeira real. Ele deve demonstrar o fluxo arquitetural correto:

- intencao de doacao;
- criacao de cobranca Pix;
- QR Code/copia-e-cola;
- webhook/simulacao de confirmacao;
- confirmacao da doacao;
- auditoria;
- atualizacao da campanha.

## Criterio de sucesso da demo

- Um avaliador consegue doar em ate 2 minutos.
- Um gestor consegue ver impacto e pendencias sem explicacao verbal longa.
- Um evento de doacao aparece em auditoria.
- A IA responde com fontes da ONG.
- A troca de tenant muda marca, dados e prioridades.
- O projeto roda localmente com um comando e tem caminho claro para cloud.
