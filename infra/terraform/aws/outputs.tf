output "cluster_name" {
  description = "EKS cluster name used by the delivery pipeline."
  value       = module.eks.cluster_name
}

output "cluster_endpoint" {
  description = "EKS API server endpoint."
  value       = module.eks.cluster_endpoint
}

output "cluster_oidc_provider_arn" {
  description = "OIDC provider ARN for workload identities."
  value       = module.eks.oidc_provider_arn
}

output "ecr_registry" {
  description = "ECR registry hostname shared by the component repositories."
  value       = split("/", values(aws_ecr_repository.component)[0].repository_url)[0]
}

output "ecr_repositories" {
  description = "Component image repository URLs."
  value       = { for name, repository in aws_ecr_repository.component : name => repository.repository_url }
}

output "configure_kubectl_command" {
  description = "Command that configures kubectl for this cluster."
  value       = "aws eks update-kubeconfig --region ${var.aws_region} --name ${module.eks.cluster_name}"
}
