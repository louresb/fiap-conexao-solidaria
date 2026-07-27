variable "aws_region" {
  description = "AWS region used by the platform."
  type        = string
  default     = "us-east-1"
}

variable "expected_aws_account_id" {
  description = "Optional account allow-list guard. Set it locally before planning or applying."
  type        = string
  default     = null
  nullable    = true

  validation {
    condition     = var.expected_aws_account_id == null || can(regex("^[0-9]{12}$", var.expected_aws_account_id))
    error_message = "expected_aws_account_id must be a 12-digit AWS account ID."
  }
}

variable "environment" {
  description = "Deployment environment name."
  type        = string
  default     = "demo"

  validation {
    condition     = contains(["development", "demo", "staging", "production"], var.environment)
    error_message = "Environment must be development, demo, staging, or production."
  }
}

variable "enable_github_oidc" {
  description = "Create repository-scoped GitHub Actions roles backed by temporary OIDC credentials."
  type        = bool
  default     = true
}

variable "github_repository" {
  description = "GitHub repository allowed to assume the CI roles, in owner/name format."
  type        = string
  default     = "louresb/fiap-conexao-solidaria"

  validation {
    condition     = can(regex("^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", var.github_repository))
    error_message = "github_repository must use owner/name format."
  }
}

variable "github_deployment_environment" {
  description = "Protected GitHub environment allowed to assume the EKS deployment role."
  type        = string
  default     = "aws"
}

variable "enable_eks" {
  description = "Provision the paid EKS, VPC, NAT gateway, and worker-node runtime. Disabled by default."
  type        = bool
  default     = false
}

variable "acknowledge_eks_costs" {
  description = "Explicit acknowledgement required when enable_eks is true."
  type        = bool
  default     = false
}

variable "cluster_name" {
  description = "EKS cluster name prefix."
  type        = string
  default     = "conexao-solidaria"
}

variable "kubernetes_namespace" {
  description = "Kubernetes namespace that the GitHub deployment role may administer."
  type        = string
  default     = "conexao-solidaria"

  validation {
    condition     = can(regex("^[a-z0-9]([-a-z0-9]*[a-z0-9])?$", var.kubernetes_namespace))
    error_message = "kubernetes_namespace must be a valid lowercase DNS label."
  }
}

variable "kubernetes_version" {
  description = "EKS Kubernetes minor version pinned for reproducible deployments."
  type        = string
  default     = "1.35"

  validation {
    condition     = can(regex("^1\\.[0-9]+$", var.kubernetes_version))
    error_message = "kubernetes_version must use the major.minor format, such as 1.35."
  }
}

variable "vpc_cidr" {
  description = "CIDR block for the platform VPC."
  type        = string
  default     = "10.40.0.0/16"

  validation {
    condition     = can(cidrhost(var.vpc_cidr, 0))
    error_message = "vpc_cidr must be a valid IPv4 CIDR block."
  }
}

variable "availability_zone_count" {
  description = "Number of availability zones used by the cluster."
  type        = number
  default     = 3

  validation {
    condition     = var.availability_zone_count >= 2 && var.availability_zone_count <= 3
    error_message = "availability_zone_count must be 2 or 3."
  }
}

variable "single_nat_gateway" {
  description = "Use one NAT gateway. Disable for one gateway per availability zone."
  type        = bool
  default     = true
}

variable "cluster_endpoint_public_access" {
  description = "Expose the EKS API endpoint publicly in addition to private access."
  type        = bool
  default     = false
}

variable "cluster_endpoint_public_access_cidrs" {
  description = "CIDRs allowed to reach the public EKS API endpoint."
  type        = list(string)
  default     = ["127.0.0.1/32"]

  validation {
    condition     = alltrue([for cidr in var.cluster_endpoint_public_access_cidrs : can(cidrhost(cidr, 0))])
    error_message = "Every endpoint access entry must be a valid CIDR block."
  }
}

variable "node_instance_types" {
  description = "Allowed EC2 instance types for the managed node group."
  type        = list(string)
  default     = ["m7i-flex.large"]
}

variable "node_min_size" {
  description = "Minimum managed node group size."
  type        = number
  default     = 2
}

variable "node_desired_size" {
  description = "Initial managed node group size."
  type        = number
  default     = 2
}

variable "node_max_size" {
  description = "Maximum managed node group size."
  type        = number
  default     = 5
}

variable "ecr_image_retention_count" {
  description = "Number of tagged images retained in each ECR repository."
  type        = number
  default     = 10

  validation {
    condition     = var.ecr_image_retention_count >= 3
    error_message = "At least three tagged images must be retained."
  }
}

variable "tags" {
  description = "Additional tags applied to all supported resources."
  type        = map(string)
  default     = {}
}
