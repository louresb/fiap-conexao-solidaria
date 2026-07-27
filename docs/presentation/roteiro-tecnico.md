# Roteiro técnico da demonstração

O vídeo deve durar entre 12 e 14 minutos. A demonstração percorre produto, arquitetura, operação e evidências; não há leitura de código linha a linha.

## Preparação

Antes de gravar:

1. Abra o [produto no AKS](https://conexao-solidaria-blouresfiap26.chilecentral.cloudapp.azure.com), o repositório, a execução canônica da pipeline e os diagramas.
2. Autentique `az`, `kubelogin`, `aws` e `kubectl`, sem exibir credenciais na gravação.
3. Selecione o AKS e confirme que todos os pods estão prontos.
4. Em terminais separados, encaminhe Grafana, RabbitMQ e Zabbix:

```powershell
$env:PATH="$env:USERPROFILE\.azure-kubelogin;$env:PATH"
az aks get-credentials --resource-group rg-conexao-solidaria-production --name aks-conexao-solidaria-production --overwrite-existing
kubelogin convert-kubeconfig -l azurecli

kubectl -n conexao-solidaria get pods
kubectl -n conexao-solidaria port-forward svc/conexao-solidaria-grafana 3000:3000
kubectl -n conexao-solidaria port-forward svc/conexao-solidaria-rabbitmq 15672:15672
kubectl -n conexao-solidaria port-forward svc/conexao-solidaria-zabbix-web 8085:8080
```

As senhas de demonstração e operação estão no Azure Key Vault `kv-conexaosolidariablour`. Recupere-as fora da gravação e não mostre os valores no terminal.

## Sequência

### 0:00 - 0:50 | Problema e produto

Abra a página inicial e apresente a Conexão Solidária como plataforma para a Esperança Solidária digitalizar campanhas, doações e prestação de contas. Mostre rapidamente a experiência pública, a troca de organização e o painel de transparência.

### 0:50 - 2:20 | Arquitetura e decisões

Mostre o diagrama principal e percorra o fluxo da esquerda para a direita: Blazor, Gateway/BFF YARP, Keycloak, APIs independentes, PostgreSQL por capacidade, RabbitMQ/MassTransit, Redis, OpenSearch, MongoDB e observabilidade. Explique que a intenção de doação é transacional com Outbox, a confirmação é idempotente e o total público é uma projeção atualizada pelo worker.

Abra o PDF de decisões de persistência e explique em uma frase a responsabilidade de PostgreSQL, Redis, OpenSearch e MongoDB. Mostre o Event Storming apenas para evidenciar que os limites e eventos vieram do domínio.

### 2:20 - 3:20 | CI/CD e supply chain

Abra a execução canônica da pipeline. Mostre `Build and test`, `Validate Helm and manifests`, o E2E, as nove imagens e os jobs de ACR/ECR. Explique que GitHub Actions autentica nas clouds por OIDC, sem access keys permanentes, e que Trivy, SBOM e provenance fazem parte da esteira.

### 3:20 - 4:20 | Kubernetes multi-cloud

No terminal, mostre os contextos e os pods:

```powershell
kubectl config get-contexts
kubectl -n conexao-solidaria get pods
kubectl -n conexao-solidaria get ingress,hpa,pdb
```

Mostre no portal Azure o AKS, ACR e Key Vault. Em seguida, mostre no portal AWS o EKS e os nove repositórios ECR. Explique que os dados são independentes, mas imagens, chart, probes, rolling update, HPA e políticas operacionais são portáveis.

### 4:20 - 5:20 | Observabilidade

Abra `http://localhost:3000` e mostre o dashboard provisionado com requisições, latência, processamento de doações e telemetria dos serviços. Abra `http://localhost:8085` para mostrar os web scenarios do Zabbix e os endpoints saudáveis. Cite Serilog, `tenantId`, `correlationId` e OpenTelemetry; no Grafana, abra um trace recente no Tempo se já houver tráfego.

### 5:20 - 6:10 | JWT e RBAC

Mostre a obtenção do token pelo endpoint OIDC do Keycloak em Postman ou PowerShell. Exiba apenas o início do token e os claims decodificados `roles` e `tenant_id`; não exponha senha nem client secret. Explique que endpoints de gestão exigem `GestorONG` e doação exige `Doador`.

### 6:10 - 7:30 | Criação de campanha

No produto, entre como gestor usando o helper recolhível de contas para avaliação. Crie uma campanha com título, descrição, datas futuras e meta positiva. Mostre a campanha na gestão e destaque que regras inválidas são rejeitadas pelo domínio.

### 7:30 - 10:10 | Doação e processamento assíncrono

Saia e entre como doador. Abra uma campanha ativa, escolha um valor e avance até o pagamento sandbox. Antes de confirmar, deixe a tela do RabbitMQ em `http://localhost:15672` pronta na visão de filas.

Confirme o pagamento no sandbox, mostre a atividade nas filas e volte ao produto. O total arrecadado deve mudar sem atualização direta pela API de doações. Abra “Minhas doações” e mostre o status confirmado. Explique o fluxo `DonationIntentCreated -> PaymentConfirmed -> DonationProcessed -> CampaignProjectionUpdated`, com Inbox/Outbox e idempotência.

### 10:10 - 11:10 | Busca e cache

Faça uma busca tolerante a erro de digitação para mostrar o OpenSearch. Em seguida, faça duas chamadas idênticas à listagem pública e mostre os headers `X-Cache: MISS` e `X-Cache: HIT`. Explique que eventos pós-commit invalidam Redis e reindexam a projeção para impedir leitura obsoleta.

### 11:10 - 12:00 | Auditoria e tracing

Abra a auditoria como gestor e filtre pelo `correlationId` da doação. Mostre `tenantId`, tipo do evento, origem, timestamp e payload redigido. No Grafana/Tempo, use o mesmo identificador para relacionar Gateway, pagamentos, worker e auditoria.

### 12:00 - 13:00 | IA fundamentada e resiliência

No assistente, pergunte como a ONG presta contas das doações. Mostre a resposta e as fontes. Explique que Azure usa geração fundamentada e que a mesma API, na AWS, mantém resposta extrativa verificável quando o provedor generativo não está configurado.

### 13:00 - 13:40 | Encerramento

Retome a topologia multi-cloud e conclua com os fatos demonstrados: autenticação e RBAC, campanha, doação assíncrona, transparência atualizada, auditoria, observabilidade, CI/CD e execução Kubernetes em Azure e AWS. Termine na página pública do produto.

## Critérios de corte

Se o vídeo ultrapassar 15 minutos, preserve obrigatoriamente arquitetura, pipeline, pods, Grafana, JWT, campanha, doação, RabbitMQ e atualização do total. Busca, cache, auditoria, tracing, IA e a segunda cloud entram depois desses critérios, nunca antes deles.
