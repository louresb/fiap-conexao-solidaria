output "resource_group_name" {
  description = "Resource group containing the Azure platform."
  value       = azurerm_resource_group.platform.name
}

output "cluster_name" {
  description = "AKS cluster name used by the delivery pipeline."
  value       = azurerm_kubernetes_cluster.platform.name
}

output "cluster_oidc_issuer_url" {
  description = "OIDC issuer URL for Azure workload identities."
  value       = azurerm_kubernetes_cluster.platform.oidc_issuer_url
}

output "container_registry_name" {
  description = "ACR name used by the delivery pipeline."
  value       = azurerm_container_registry.platform.name
}

output "container_registry_login_server" {
  description = "ACR login server."
  value       = azurerm_container_registry.platform.login_server
}

output "key_vault_name" {
  description = "Key Vault used by the AKS Secrets Store CSI provider."
  value       = azurerm_key_vault.platform.name
}

output "key_vault_uri" {
  description = "URI of the platform Key Vault."
  value       = azurerm_key_vault.platform.vault_uri
}

output "key_vault_csi_client_id" {
  description = "Client ID used by the AKS Secrets Store CSI provider."
  value       = azurerm_kubernetes_cluster.platform.key_vault_secrets_provider[0].secret_identity[0].client_id
}

output "ingress_public_ip_name" {
  description = "Static public IP resource consumed by the AKS application routing controller."
  value       = azurerm_public_ip.ingress.name
}

output "ingress_public_ip_address" {
  description = "Static public address reserved for the product ingress."
  value       = azurerm_public_ip.ingress.ip_address
}

output "application_hostname" {
  description = "Azure-managed DNS hostname used by the product and identity provider."
  value       = azurerm_public_ip.ingress.fqdn
}

output "github_azure_client_id" {
  description = "AZURE_CLIENT_ID GitHub variable for secretless OIDC authentication."
  value       = azurerm_user_assigned_identity.github_delivery.client_id
}

output "github_azure_tenant_id" {
  description = "AZURE_TENANT_ID GitHub variable for secretless OIDC authentication."
  value       = data.azurerm_client_config.current.tenant_id
}

output "github_azure_subscription_id" {
  description = "AZURE_SUBSCRIPTION_ID GitHub variable for secretless OIDC authentication."
  value       = data.azurerm_subscription.current.subscription_id
}

output "configure_kubectl_command" {
  description = "Command that configures kubectl for this cluster."
  value       = "az aks get-credentials --resource-group ${azurerm_resource_group.platform.name} --name ${azurerm_kubernetes_cluster.platform.name} --overwrite-existing"
}
