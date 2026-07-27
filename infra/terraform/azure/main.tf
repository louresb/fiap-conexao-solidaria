locals {
  name           = "${var.name_prefix}-${var.environment}"
  acr_name       = coalesce(var.container_registry_name, replace("${var.name_prefix}${var.environment}", "-", ""))
  key_vault_name = "kv-${substr(local.acr_name, 0, 21)}"

  common_tags = merge({
    Project     = "ConexaoSolidaria"
    Environment = var.environment
    ManagedBy   = "Terraform"
  }, var.tags)
}

resource "terraform_data" "aks_cost_guard" {
  input = var.acknowledge_aks_costs

  lifecycle {
    precondition {
      condition     = var.acknowledge_aks_costs
      error_message = "AKS worker nodes, disks, public IP, registry, and telemetry incur recurring costs. Set acknowledge_aks_costs=true only for an intentional deployment window."
    }
  }
}

resource "azurerm_resource_group" "platform" {
  name     = "rg-${local.name}"
  location = var.location
  tags     = local.common_tags
}

resource "azurerm_virtual_network" "platform" {
  name                = "vnet-${local.name}"
  location            = azurerm_resource_group.platform.location
  resource_group_name = azurerm_resource_group.platform.name
  address_space       = [var.vnet_cidr]
  tags                = local.common_tags
}

resource "azurerm_subnet" "aks" {
  name                 = "snet-aks"
  resource_group_name  = azurerm_resource_group.platform.name
  virtual_network_name = azurerm_virtual_network.platform.name
  address_prefixes     = [var.aks_subnet_cidr]
  service_endpoints    = ["Microsoft.KeyVault"]
}

resource "azurerm_log_analytics_workspace" "platform" {
  name                = "log-${local.name}"
  location            = azurerm_resource_group.platform.location
  resource_group_name = azurerm_resource_group.platform.name
  sku                 = "PerGB2018"
  retention_in_days   = 30
  daily_quota_gb      = var.log_analytics_daily_quota_gb
  tags                = local.common_tags
}

resource "azurerm_container_registry" "platform" {
  name                = local.acr_name
  resource_group_name = azurerm_resource_group.platform.name
  location            = azurerm_resource_group.platform.location
  sku                 = "Basic"
  admin_enabled       = false
  tags                = local.common_tags
}

data "azurerm_client_config" "current" {}
data "azurerm_subscription" "current" {}

resource "azurerm_key_vault" "platform" {
  name                       = local.key_vault_name
  location                   = azurerm_resource_group.platform.location
  resource_group_name        = azurerm_resource_group.platform.name
  tenant_id                  = data.azurerm_client_config.current.tenant_id
  sku_name                   = "standard"
  rbac_authorization_enabled = true
  purge_protection_enabled   = false
  soft_delete_retention_days = 7
  tags                       = local.common_tags

  network_acls {
    bypass                     = "AzureServices"
    default_action             = "Deny"
    ip_rules                   = var.api_server_authorized_ip_ranges
    virtual_network_subnet_ids = [azurerm_subnet.aks.id]
  }
}

resource "azurerm_public_ip" "ingress" {
  name                = "pip-${local.name}"
  location            = azurerm_resource_group.platform.location
  resource_group_name = azurerm_resource_group.platform.name
  allocation_method   = "Static"
  sku                 = "Standard"
  domain_name_label   = var.ingress_dns_label
  tags                = local.common_tags
}

resource "azurerm_kubernetes_cluster" "platform" {
  name                = "aks-${local.name}"
  location            = azurerm_resource_group.platform.location
  resource_group_name = azurerm_resource_group.platform.name
  dns_prefix          = local.name
  kubernetes_version  = var.kubernetes_version
  sku_tier            = "Free"
  support_plan        = "KubernetesOfficial"

  role_based_access_control_enabled = true
  local_account_disabled            = true
  oidc_issuer_enabled               = true
  workload_identity_enabled         = true
  azure_policy_enabled              = true
  image_cleaner_enabled             = true
  image_cleaner_interval_hours      = 168
  automatic_upgrade_channel         = "stable"
  node_os_upgrade_channel           = "NodeImage"

  azure_active_directory_role_based_access_control {
    azure_rbac_enabled = true
    tenant_id          = data.azurerm_client_config.current.tenant_id
  }

  api_server_access_profile {
    authorized_ip_ranges = var.api_server_authorized_ip_ranges
  }

  default_node_pool {
    name                        = "system"
    vm_size                     = var.node_vm_size
    vnet_subnet_id              = azurerm_subnet.aks.id
    auto_scaling_enabled        = var.node_max_count > var.node_min_count
    node_count                  = var.node_max_count == var.node_min_count ? var.node_min_count : null
    min_count                   = var.node_max_count > var.node_min_count ? var.node_min_count : null
    max_count                   = var.node_max_count > var.node_min_count ? var.node_max_count : null
    os_disk_size_gb             = var.node_os_disk_size_gb
    os_disk_type                = "Managed"
    os_sku                      = "AzureLinux3"
    temporary_name_for_rotation = "systemtmp"

    upgrade_settings {
      max_surge = "33%"
    }
  }

  identity {
    type = "SystemAssigned"
  }

  key_vault_secrets_provider {
    secret_rotation_enabled  = true
    secret_rotation_interval = "2m"
  }

  web_app_routing {
    dns_zone_ids             = []
    default_nginx_controller = "None"
  }

  network_profile {
    network_plugin      = "azure"
    network_plugin_mode = "overlay"
    network_data_plane  = "cilium"
    network_policy      = "cilium"
    load_balancer_sku   = "standard"
    outbound_type       = "loadBalancer"
    service_cidr        = "10.60.0.0/16"
    dns_service_ip      = "10.60.0.10"
  }

  oms_agent {
    log_analytics_workspace_id      = azurerm_log_analytics_workspace.platform.id
    msi_auth_for_monitoring_enabled = true
  }

  tags = local.common_tags

  depends_on = [terraform_data.aks_cost_guard]
}

resource "azurerm_kubernetes_cluster_node_pool" "workloads" {
  name                  = "apps"
  kubernetes_cluster_id = azurerm_kubernetes_cluster.platform.id
  vm_size               = var.workload_node_vm_size
  node_count            = var.workload_node_count
  mode                  = "User"
  vnet_subnet_id        = azurerm_subnet.aks.id
  max_pods              = 50
  os_disk_size_gb       = 128
  os_disk_type          = "Managed"
  os_sku                = "Ubuntu"
  orchestrator_version  = var.kubernetes_version

  upgrade_settings {
    max_surge                 = "10%"
    undrainable_node_behavior = "Schedule"
  }

  tags = local.common_tags
}

resource "azurerm_role_assignment" "aks_acr_pull" {
  scope                            = azurerm_container_registry.platform.id
  role_definition_name             = "AcrPull"
  principal_id                     = azurerm_kubernetes_cluster.platform.kubelet_identity[0].object_id
  skip_service_principal_aad_check = true
}

resource "azurerm_role_assignment" "aks_ingress_network" {
  scope                = azurerm_resource_group.platform.id
  role_definition_name = "Network Contributor"
  principal_id         = azurerm_kubernetes_cluster.platform.identity[0].principal_id
}

resource "azurerm_role_assignment" "aks_admin" {
  for_each = var.aks_admin_principal_object_ids

  scope                = azurerm_kubernetes_cluster.platform.id
  role_definition_name = "Azure Kubernetes Service RBAC Cluster Admin"
  principal_id         = each.value
}

resource "azurerm_role_assignment" "aks_key_vault_secrets" {
  scope                = azurerm_key_vault.platform.id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_kubernetes_cluster.platform.key_vault_secrets_provider[0].secret_identity[0].object_id
}

resource "azurerm_role_assignment" "operator_key_vault_admin" {
  for_each = var.aks_admin_principal_object_ids

  scope                = azurerm_key_vault.platform.id
  role_definition_name = "Key Vault Administrator"
  principal_id         = each.value
}

resource "azurerm_user_assigned_identity" "github_delivery" {
  name                = "id-github-${local.name}"
  location            = azurerm_resource_group.platform.location
  resource_group_name = azurerm_resource_group.platform.name
  tags                = local.common_tags
}

resource "azurerm_federated_identity_credential" "github_main" {
  name                      = "github-main"
  audience                  = ["api://AzureADTokenExchange"]
  issuer                    = "https://token.actions.githubusercontent.com"
  subject                   = "repo:${var.github_repository}:ref:refs/heads/main"
  user_assigned_identity_id = azurerm_user_assigned_identity.github_delivery.id
}

resource "azurerm_federated_identity_credential" "github_environment" {
  name                      = "github-environment-azure"
  audience                  = ["api://AzureADTokenExchange"]
  issuer                    = "https://token.actions.githubusercontent.com"
  subject                   = "repo:${var.github_repository}:environment:azure"
  user_assigned_identity_id = azurerm_user_assigned_identity.github_delivery.id
}

resource "azurerm_role_assignment" "github_acr_push" {
  scope                            = azurerm_container_registry.platform.id
  role_definition_name             = "AcrPush"
  principal_id                     = azurerm_user_assigned_identity.github_delivery.principal_id
  skip_service_principal_aad_check = true
}

resource "azurerm_role_assignment" "github_aks_command" {
  scope                            = azurerm_kubernetes_cluster.platform.id
  role_definition_name             = "Azure Kubernetes Service Contributor Role"
  principal_id                     = azurerm_user_assigned_identity.github_delivery.principal_id
  skip_service_principal_aad_check = true
}

resource "azurerm_role_assignment" "github_aks_deployer" {
  scope                            = azurerm_kubernetes_cluster.platform.id
  role_definition_name             = "Azure Kubernetes Service RBAC Cluster Admin"
  principal_id                     = azurerm_user_assigned_identity.github_delivery.principal_id
  skip_service_principal_aad_check = true
}
