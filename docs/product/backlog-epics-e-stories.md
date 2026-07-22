# Backlog inicial

## Epic 1 - Fundacao do produto

- Como visitante, quero entender a proposta da ONG em uma pagina publica.
- Como avaliador, quero acessar uma demo sem configurar dados manualmente.
- Como time, queremos uma solucao .NET organizada para evoluir em modulos.

## Epic 2 - Multi-tenancy

- Como administrador SaaS, quero cadastrar tenants com tema e status.
- Como usuario, quero acessar apenas dados do meu tenant.
- Como avaliador, quero trocar tenant e perceber dados e visual diferentes.

## Epic 3 - Campanhas

- Como gestor, quero criar rascunho de campanha.
- Como gestor, quero publicar campanha aprovada.
- Como doador, quero ver campanhas ativas.
- Como gestor, quero acompanhar campanhas em risco.

## Epic 4 - Doacoes

- Como doador, quero iniciar uma doacao simples.
- Como gestor, quero ver total arrecadado atualizado.
- Como sistema, quero processar doacoes de forma assincrona.
- Como auditor, quero rastrear status de uma doacao.
- Como doador, quero pagar com Pix usando QR Code ou copia-e-cola.
- Como sistema, quero confirmar pagamento por webhook idempotente.
- Como gestor, quero ver doacoes aguardando Pix, confirmadas, expiradas e falhas.

## Epic 4.1 - Pagamentos

- Como arquiteto, quero abstrair provedores de pagamento sem acoplar o dominio a um gateway.
- Como administrador, quero configurar gateway por tenant sem segredo hardcoded.
- Como sistema, quero registrar eventos financeiros em trilha auditavel.
- Como avaliador, quero simular uma confirmacao Pix sem depender de dinheiro real.

## Epic 5 - Transparencia e auditoria

- Como doador, quero ver como os recursos serao usados.
- Como gestor, quero publicar evidencias e relatorios.
- Como auditor, quero consultar trilha de eventos.
- Como sistema, quero registrar alteracoes criticas de forma append-only.

## Epic 6 - IA assistida

- Como gestor, quero perguntar sobre uma campanha e receber resposta com fontes.
- Como gestor, quero sugestoes de engajamento com justificativa.
- Como doador, quero resumo do meu impacto.
- Como administrador, quero auditar prompts, fontes e respostas.
