variable "aws_region" {
  description = "AWS region used by the platform."
  type        = string
  default     = "us-east-1"
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

variable "cluster_name" {
  description = "EKS cluster name."
  type        = string
  default     = "conexao-solidaria"
}

variable "kubernetes_version" {
  description = "Optional EKS Kubernetes minor version. Null selects the current EKS default."
  type        = string
  default     = null
  nullable    = true
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
  default     = ["t3.large"]
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
  default     = 20

  validation {
    condition     = var.ecr_image_retention_count >= 5
    error_message = "At least five tagged images must be retained."
  }
}

variable "tags" {
  description = "Additional tags applied to all supported resources."
  type        = map(string)
  default     = {}
}
