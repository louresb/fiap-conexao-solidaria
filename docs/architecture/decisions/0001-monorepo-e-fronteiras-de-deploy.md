# ADR 0001: Monorepo e fronteiras de deploy

- Status: aceita
- Data: 2026-07-26

## Contexto

Uma única equipe mantém produto, serviços, contratos, chart Helm e automação. Mudanças de eventos e de configuração frequentemente atravessam mais de um componente, mas cada runtime precisa escalar e ser implantado sem recompilar os demais.

## Decisão

Adotar um monorepo com projetos .NET, Dockerfiles, imagens e Deployments independentes. Contratos de integração vivem em `src/Contracts`; defaults estritamente operacionais vivem em `src/Platform`. Regras de domínio não são compartilhadas entre capacidades.

Não usar Git submodules nem um pacote NuGet privado para o cliente RabbitMQ enquanto houver uma única equipe e um único ciclo de produto. MassTransit já fornece a abstração de transporte; uma biblioteca interna adicional criaria versionamento e compatibilidade sem autonomia organizacional correspondente.

## Consequências

- PRs podem alterar contrato, produtor, consumidor e infraestrutura de forma atômica.
- CI detecta os componentes afetados, mas cada imagem mantém ciclo de deploy próprio.
- A extração para repositórios ou pacotes separados permanece possível quando houver ownership e cadência independentes.
