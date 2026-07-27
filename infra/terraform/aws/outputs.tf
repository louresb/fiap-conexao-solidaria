output "aws_account_id" {
  description = "AWS account validated by the provider."
  value       = data.aws_caller_identity.current.account_id
}

output "aws_region" {
  description = "AWS region used by the platform."
  value       = var.aws_region
}

output "cluster_name" {
  description = "EKS cluster name used by the delivery pipeline, or null when EKS is disabled."
  value       = var.enable_eks ? module.eks[0].cluster_name : null
}

output "cluster_endpoint" {
  description = "EKS API server endpoint, or null when EKS is disabled."
  value       = var.enable_eks ? module.eks[0].cluster_endpoint : null
}

output "cluster_oidc_provider_arn" {
  description = "EKS workload identity provider ARN, or null when EKS is disabled."
  value       = var.enable_eks ? module.eks[0].oidc_provider_arn : null
}

output "runtime_secret_arn" {
  description = "Secrets Manager ARN synchronized into Kubernetes, or null when EKS is disabled."
  value       = var.enable_eks ? aws_secretsmanager_secret.runtime[0].arn : null
}

output "runtime_secret_name" {
  description = "Secrets Manager name initialized by the AWS bootstrap script, or null when EKS is disabled."
  value       = var.enable_eks ? aws_secretsmanager_secret.runtime[0].name : null
}

output "ecr_registry" {
  description = "ECR registry hostname shared by the component repositories."
  value       = split("/", values(aws_ecr_repository.component)[0].repository_url)[0]
}

output "ecr_repositories" {
  description = "Component image repository URLs."
  value       = { for name, repository in aws_ecr_repository.component : name => repository.repository_url }
}

output "github_actions_ecr_role_arn" {
  description = "Repository-scoped OIDC role used to publish component images."
  value       = var.enable_github_oidc ? aws_iam_role.github_actions_ecr[0].arn : null
}

output "github_actions_eks_role_arn" {
  description = "Environment-scoped OIDC role used to deploy to EKS, or null when EKS is disabled."
  value       = var.enable_eks && var.enable_github_oidc ? aws_iam_role.github_actions_eks[0].arn : null
}

output "configure_kubectl_command" {
  description = "Command that configures kubectl, or null when EKS is disabled."
  value       = var.enable_eks ? "aws eks update-kubeconfig --region ${var.aws_region} --name ${module.eks[0].cluster_name}" : null
}
