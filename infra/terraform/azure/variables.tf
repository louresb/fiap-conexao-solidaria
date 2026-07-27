variable "location" {
  description = "Azure region used by the platform."
  type        = string
  default     = "Chile Central"
}

variable "acknowledge_aks_costs" {
  description = "Explicit acknowledgement required before planning or provisioning the paid AKS runtime."
  type        = bool
  default     = false
}

variable "environment" {
  description = "Deployment environment name."
  type        = string
  default     = "production"

  validation {
    condition     = contains(["development", "staging", "production"], var.environment)
    error_message = "Environment must be development, staging, or production."
  }
}

variable "name_prefix" {
  description = "Prefix used by Azure resources."
  type        = string
  default     = "conexao-solidaria"
}

variable "container_registry_name" {
  description = "Globally unique ACR name. Null derives a name from name_prefix and environment."
  type        = string
  default     = null
  nullable    = true

  validation {
    condition = (
      var.container_registry_name == null ||
      can(regex("^[a-zA-Z0-9]{5,50}$", var.container_registry_name))
    )
    error_message = "container_registry_name must contain 5-50 alphanumeric characters."
  }
}

variable "ai_services_enabled" {
  description = "Whether to provision the pay-per-token Azure AI account and grounded-generation deployment."
  type        = bool
  default     = false
}

variable "ai_services_name" {
  description = "Globally unique Azure AI Services account name. Null derives a name from the ACR name."
  type        = string
  default     = null
  nullable    = true

  validation {
    condition = (
      var.ai_services_name == null ||
      can(regex("^[a-z0-9][a-z0-9-]{1,62}[a-z0-9]$", var.ai_services_name))
    )
    error_message = "ai_services_name must contain 3-64 lowercase letters, numbers, or hyphens."
  }
}

variable "ai_services_location" {
  description = "Azure region used by the grounded-generation model."
  type        = string
  default     = "East US 2"
}

variable "ai_model_deployment_name" {
  description = "Deployment name sent as the model in OpenAI-compatible requests."
  type        = string
  default     = "gpt-5-4-mini"
}

variable "ai_model_name" {
  description = "Azure AI model name used for grounded generation."
  type        = string
  default     = "gpt-5.4-mini"
}

variable "ai_model_version" {
  description = "Pinned Azure AI model version."
  type        = string
  default     = "2026-03-17"
}

variable "ai_model_capacity" {
  description = "Small GlobalStandard token capacity used by the demonstration environment."
  type        = number
  default     = 1

  validation {
    condition     = var.ai_model_capacity >= 1 && var.ai_model_capacity <= 10
    error_message = "ai_model_capacity must be between 1 and 10."
  }
}

variable "kubernetes_version" {
  description = "AKS Kubernetes minor version. Azure resolves the latest supported patch."
  type        = string
  default     = "1.35"
  nullable    = true
}

variable "vnet_cidr" {
  description = "CIDR block for the platform virtual network."
  type        = string
  default     = "10.50.0.0/16"

  validation {
    condition     = can(cidrhost(var.vnet_cidr, 0))
    error_message = "vnet_cidr must be a valid CIDR block."
  }
}

variable "aks_subnet_cidr" {
  description = "CIDR block used by AKS nodes."
  type        = string
  default     = "10.50.0.0/20"

  validation {
    condition     = can(cidrhost(var.aks_subnet_cidr, 0))
    error_message = "aks_subnet_cidr must be a valid CIDR block."
  }
}

variable "node_vm_size" {
  description = "Virtual machine size for the AKS system pool."
  type        = string
  default     = "Standard_B4as_v2"
}

variable "node_min_count" {
  description = "Minimum AKS system pool size."
  type        = number
  default     = 1

  validation {
    condition     = var.node_min_count >= 1
    error_message = "The AKS system pool requires at least one node."
  }
}

variable "node_max_count" {
  description = "Maximum AKS system pool size."
  type        = number
  default     = 1

  validation {
    condition     = var.node_max_count >= var.node_min_count
    error_message = "node_max_count must be greater than or equal to node_min_count."
  }
}

variable "workload_node_vm_size" {
  description = "Virtual machine size for the AKS application pool."
  type        = string
  default     = "Standard_B2as_v2"
}

variable "workload_node_count" {
  description = "Fixed number of nodes in the AKS application pool."
  type        = number
  default     = 1

  validation {
    condition     = var.workload_node_count >= 1
    error_message = "workload_node_count must be at least one."
  }
}

variable "ingress_dns_label" {
  description = "Globally unique label used by the public AKS ingress FQDN."
  type        = string

  validation {
    condition     = can(regex("^[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$", var.ingress_dns_label))
    error_message = "ingress_dns_label must contain 3-63 lowercase letters, numbers, or hyphens."
  }
}

variable "github_repository" {
  description = "GitHub owner/repository trusted by Azure workload identity federation."
  type        = string
  default     = "louresb/fiap-conexao-solidaria"

  validation {
    condition     = can(regex("^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", var.github_repository))
    error_message = "github_repository must use the owner/repository format."
  }
}

variable "node_os_disk_size_gb" {
  description = "Managed OS disk size for each AKS node."
  type        = number
  default     = 64

  validation {
    condition     = var.node_os_disk_size_gb >= 30
    error_message = "node_os_disk_size_gb must be at least 30 GB."
  }
}

variable "log_analytics_daily_quota_gb" {
  description = "Daily Log Analytics ingestion cap used to control demo environment cost."
  type        = number
  default     = 0.5

  validation {
    condition     = var.log_analytics_daily_quota_gb >= 0.1
    error_message = "log_analytics_daily_quota_gb must be at least 0.1 GB."
  }
}

variable "aks_admin_principal_object_ids" {
  description = "Microsoft Entra principal object IDs granted AKS RBAC cluster administration."
  type        = set(string)

  validation {
    condition     = length(var.aks_admin_principal_object_ids) > 0
    error_message = "At least one AKS administrator principal object ID is required."
  }
}

variable "api_server_authorized_ip_ranges" {
  description = "Operator CIDRs allowed to reach the AKS API server and Azure Key Vault."
  type        = list(string)

  validation {
    condition = (
      length(var.api_server_authorized_ip_ranges) > 0 &&
      alltrue([for cidr in var.api_server_authorized_ip_ranges : can(cidrhost(cidr, 0))])
    )
    error_message = "At least one valid operator CIDR is required."
  }
}

variable "tags" {
  description = "Additional tags applied to all supported resources."
  type        = map(string)
  default     = {}
}
