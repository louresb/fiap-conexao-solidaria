# ADR 0006: Dependências, DNS/TLS e segredos

- Status: aceita
- Data: 2026-07-26

## Contexto

Serviços gerenciados para todos os motores aumentariam custo e diferenças entre provedores. O produto ainda precisa de URL estável, TLS e segredos fora do código.

## Decisão

Executar PostgreSQL, Redis, RabbitMQ, MongoDB, OpenSearch, Keycloak e observabilidade dentro do cluster, com PVCs e limites de recursos. Essa escolha prioriza paridade e um custo previsível para a carga atual; cada dependência conserva uma interface que permite migração posterior para serviço gerenciado.

Na Azure, IP estático e label DNS expõem produto e Keycloak no mesmo host; `/auth` é roteado ao Keycloak. `cert-manager` emite e renova o certificado. Segredos ficam no Azure Key Vault e chegam aos pods pelo Secrets Store CSI.

Na AWS, o EKS sob demanda usa EBS CSI para os PVCs, NLB com `ingress-nginx`, `cert-manager` e Secrets Manager sincronizado pelo AWS Secrets and Configuration Provider. O acesso do sincronizador ao segredo usa EKS Pod Identity com privilégio mínimo. Para demonstração, o bootstrap pode derivar um hostname `sslip.io` do NLB; um domínio próprio em Route 53 substitui esse endereço sem alterar o chart.

## Consequências

- A equipe opera backup, patching e capacidade dos motores no cluster.
- Uma única origem HTTPS simplifica redirect URIs, cookies e CORS.
- A migração para RDS, ElastiCache, Amazon OpenSearch ou equivalentes Azure será decidida por SLA, escala e custo medidos.
