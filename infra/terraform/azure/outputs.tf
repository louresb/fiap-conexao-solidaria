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

output "configure_kubectl_command" {
  description = "Command that configures kubectl for this cluster."
  value       = "az aks get-credentials --resource-group ${azurerm_resource_group.platform.name} --name ${azurerm_kubernetes_cluster.platform.name} --overwrite-existing"
}
