variable "location" {
  description = "Azure region used by the platform."
  type        = string
  default     = "Brazil South"
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

variable "kubernetes_version" {
  description = "Optional AKS Kubernetes version. Null selects the current recommended version."
  type        = string
  default     = null
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
  default     = "Standard_D2s_v5"
}

variable "node_min_count" {
  description = "Minimum AKS system pool size."
  type        = number
  default     = 2
}

variable "node_max_count" {
  description = "Maximum AKS system pool size."
  type        = number
  default     = 5
}

variable "api_server_authorized_ip_ranges" {
  description = "CIDRs allowed to reach the AKS API server. Replace the default before provisioning."
  type        = list(string)
  default     = ["127.0.0.1/32"]

  validation {
    condition     = length(var.api_server_authorized_ip_ranges) > 0 && alltrue([for cidr in var.api_server_authorized_ip_ranges : can(cidrhost(cidr, 0))])
    error_message = "At least one valid API server CIDR must be provided."
  }
}

variable "tags" {
  description = "Additional tags applied to all supported resources."
  type        = map(string)
  default     = {}
}
