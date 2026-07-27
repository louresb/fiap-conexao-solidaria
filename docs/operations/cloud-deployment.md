# Publicação Cloud

A plataforma usa um único chart Helm e imagens independentes para os nove workloads. Azure e AWS mantêm dados separados; a portabilidade não implica replicação active-active entre provedores.

## Dependências

PostgreSQL, Redis, RabbitMQ, MongoDB, OpenSearch, Keycloak, Loki, Tempo, Prometheus, Grafana e Zabbix são executados no cluster nesta versão. PostgreSQL, RabbitMQ, MongoDB e OpenSearch preservam dados em PVCs da StorageClass padrão. Cache e telemetria usam `emptyDir` no perfil cloud para respeitar os limites de disco do nó econômico; podem perder histórico quando um pod é recriado. A evolução para serviços gerenciados preserva os contratos de conexão, mensageria e observabilidade.

## Azure

O perfil Azure cria AKS Free, ACR Basic, Key Vault, Log Analytics, rede, IP público com hostname Azure e identidade federada do GitHub. Segredos são sincronizados pelo Secrets Store CSI.

```powershell
./scripts/Initialize-TerraformBackend.ps1 -Provider azure -AzureLocation brazilsouth -AzureSuffix <sufixo-unico> -ExpectedAzureSubscriptionId <subscription-id> -ConfirmCloudMutation
cd infra/terraform/azure
terraform plan -var="acknowledge_aks_costs=true" -out azure.tfplan
terraform apply azure.tfplan
cd ../../..
.\scripts\Configure-GitHubCloudVariables.ps1 -Provider azure -ConfirmGitHubMutation
.\scripts\Publish-CloudImages.ps1 -Provider azure -ImageTag <sha-ou-tag>
.\scripts\Initialize-AzureSecrets.ps1
.\scripts\Deploy-Azure.ps1 -ImageTag <sha-ou-tag>
```

O reconhecimento de custo libera somente o plano salvo; nenhum `apply` deve ser executado antes da revisão de SKU, quota e estimativa no Azure Cost Management. Para encerrar a janela:

```powershell
terraform -chdir=infra/terraform/azure destroy
```

## AWS

Com `enable_eks=false`, Terraform cria somente os nove repositórios ECR e as roles OIDC de publicação. EKS, VPC, NAT, nós, EBS CSI e Secrets Manager exigem habilitação e confirmação explícitas no `terraform.tfvars`.

```powershell
aws login --profile conexao-solidaria-terraform
$env:AWS_PROFILE = "conexao-solidaria-terraform"
./scripts/Initialize-TerraformBackend.ps1 -Provider aws -AwsProfile conexao-solidaria-terraform -ExpectedAwsAccountId <account-id> -ConfirmCloudMutation
terraform -chdir=infra/terraform/aws plan
terraform -chdir=infra/terraform/aws apply
.\scripts\Configure-GitHubCloudVariables.ps1 -Provider aws -AwsAppHost <host-publico> -ConfirmGitHubMutation
.\scripts\Publish-CloudImages.ps1 -Provider aws -AwsProfile conexao-solidaria -ImageTag <sha-ou-tag>
.\scripts\Deploy-Aws.ps1 -ImageTag <sha-ou-tag>
```

O bootstrap instala Secrets Store CSI/ASCP, `ingress-nginx`, NLB e `cert-manager`. Sem `-AppHost`, usa um hostname temporário `sslip.io`; para endereço próprio, aponte o DNS ao hostname do NLB e passe `-AppHost doacoes.exemplo.org`.

Os backends remotos armazenam estado criptografado e versionado em Azure Blob e S3. O Blob mantém retenção de exclusão por 30 dias; o S3 mantém versionamento e bloqueia transporte sem TLS. A AWS usa o lockfile nativo do backend S3; a Azure usa leases do Blob Storage. Os arquivos HCL gerados permanecem em `.local/terraform-backends` e nunca são versionados.
O backend Azure pode residir em uma região diferente do AKS; no ambiente acadêmico ele usa `Brazil South`, aceita pela política da assinatura, sem alterar a portabilidade do workload.

Para encerrar o ambiente pago:

```powershell
terraform -chdir=infra/terraform/aws destroy
```

## GitHub Actions

Nenhuma access key ou client secret cloud é armazenada no GitHub. Terraform cria relações OIDC limitadas ao repositório, à branch `main` e aos environments protegidos.

| Provider | Variables |
|---|---|
| Azure | `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `ACR_NAME`, `AKS_RESOURCE_GROUP`, `AKS_CLUSTER`, `AZURE_INGRESS_PUBLIC_IP_NAME`, `AZURE_APP_HOST`, `K8S_NAMESPACE` |
| AWS | `AWS_GITHUB_PUBLISH_ROLE_ARN`, `AWS_GITHUB_DEPLOY_ROLE_ARN`, `AWS_REGION`, `AWS_ACCOUNT_ID`, `EKS_CLUSTER`, `AWS_APP_HOST`, `K8S_NAMESPACE` |

Flags de execução:

- `ENABLE_AZURE_PUBLISH` e `ENABLE_AWS_PUBLISH` espelham imagens do GHCR.
- `ENABLE_AZURE_DEPLOY` e `ENABLE_AWS_DEPLOY` liberam o rolling update no cluster.
- Os environments `azure` e `aws` podem exigir aprovação manual antes do deploy.

Os valores vêm dos outputs em `infra/terraform/<provider>`. As flags de deploy só devem ser ativadas depois do bootstrap inicial e do teste de readiness.
O script `Configure-GitHubCloudVariables.ps1` mantém todas as flags desligadas por padrão; use `-EnablePublish` ou `-EnableDeploy` somente na janela de entrega correspondente.
