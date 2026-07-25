locals {
  name     = "${var.name_prefix}-${var.environment}"
  acr_name = coalesce(var.container_registry_name, replace("${var.name_prefix}${var.environment}", "-", ""))

  common_tags = merge({
    Project     = "ConexaoSolidaria"
    Environment = var.environment
    ManagedBy   = "Terraform"
  }, var.tags)
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
}

resource "azurerm_log_analytics_workspace" "platform" {
  name                = "log-${local.name}"
  location            = azurerm_resource_group.platform.location
  resource_group_name = azurerm_resource_group.platform.name
  sku                 = "PerGB2018"
  retention_in_days   = 30
  tags                = local.common_tags
}

resource "azurerm_container_registry" "platform" {
  name                = local.acr_name
  resource_group_name = azurerm_resource_group.platform.name
  location            = azurerm_resource_group.platform.location
  sku                 = "Standard"
  admin_enabled       = false
  tags                = local.common_tags
}

resource "azurerm_kubernetes_cluster" "platform" {
  name                = "aks-${local.name}"
  location            = azurerm_resource_group.platform.location
  resource_group_name = azurerm_resource_group.platform.name
  dns_prefix          = local.name
  kubernetes_version  = var.kubernetes_version

  role_based_access_control_enabled = true
  oidc_issuer_enabled               = true
  workload_identity_enabled         = true
  azure_policy_enabled              = true
  image_cleaner_enabled             = true
  image_cleaner_interval_hours      = 168
  automatic_upgrade_channel         = "stable"
  node_os_upgrade_channel           = "NodeImage"

  api_server_access_profile {
    authorized_ip_ranges = var.api_server_authorized_ip_ranges
  }

  default_node_pool {
    name                 = "system"
    vm_size              = var.node_vm_size
    vnet_subnet_id       = azurerm_subnet.aks.id
    auto_scaling_enabled = true
    min_count            = var.node_min_count
    max_count            = var.node_max_count
    os_disk_type         = "Managed"
    os_sku               = "AzureLinux"

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
}

resource "azurerm_role_assignment" "aks_acr_pull" {
  scope                            = azurerm_container_registry.platform.id
  role_definition_name             = "AcrPull"
  principal_id                     = azurerm_kubernetes_cluster.platform.kubelet_identity[0].object_id
  skip_service_principal_aad_check = true
}
