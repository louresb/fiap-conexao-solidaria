# ADR 0005: Portabilidade multicloud

- Status: aceita
- Data: 2026-07-26

## Contexto

A solução precisa executar em Azure, AWS e ambiente local sem manter três arquiteturas de aplicação divergentes.

## Decisão

Kubernetes é o contrato de runtime e um único chart Helm recebe values para `local`, `azure` e `aws`. Terraform provisiona somente recursos de plataforma específicos: identidade federada, registry, cluster, rede, segredos e entrada pública.

Cada cluster mantém dados independentes. A arquitetura não declara active-active nem consistência global distribuída. Azure AKS é o ambiente público contínuo; AWS ECR e a topologia EKS permanecem implantáveis sob demanda com o mesmo artefato.

## Consequências

- Imagens e manifests são portáveis; identidade, rede e storage classes continuam específicos do provedor.
- Falha de um ambiente não promove dados automaticamente para outro.
- Uma evolução active-active exigirá replicação, resolução de conflito e objetivos de RPO/RTO próprios.
