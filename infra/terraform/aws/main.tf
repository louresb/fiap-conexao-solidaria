data "aws_partition" "current" {}

data "aws_caller_identity" "current" {}

data "aws_availability_zones" "available" {
  count = var.enable_eks ? 1 : 0

  state = "available"

  filter {
    name   = "opt-in-status"
    values = ["opt-in-not-required"]
  }
}

locals {
  name = "${var.cluster_name}-${var.environment}"
  azs = var.enable_eks ? slice(
    data.aws_availability_zones.available[0].names,
    0,
    var.availability_zone_count,
  ) : []

  components = toset([
    "web",
    "gateway",
    "identity-api",
    "campaigns-api",
    "campaigns-worker",
    "payments-api",
    "donations-api",
    "audit-api",
    "knowledge-api",
  ])

  github_main_subject       = "repo:${var.github_repository}:ref:refs/heads/main"
  github_deployment_subject = "repo:${var.github_repository}:environment:${var.github_deployment_environment}"
  cluster_arn               = "arn:${data.aws_partition.current.partition}:eks:${var.aws_region}:${data.aws_caller_identity.current.account_id}:cluster/${local.name}"
  pod_identity_assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect = "Allow"
      Principal = {
        Service = "pods.eks.amazonaws.com"
      }
      Action = [
        "sts:AssumeRole",
        "sts:TagSession",
      ]
    }]
  })

  common_tags = merge({
    Project     = "ConexaoSolidaria"
    Environment = var.environment
    ManagedBy   = "Terraform"
  }, var.tags)
}

resource "aws_iam_role" "ebs_csi" {
  count = var.enable_eks ? 1 : 0

  name               = "${local.name}-ebs-csi"
  assume_role_policy = local.pod_identity_assume_role_policy

  tags = local.common_tags
}

resource "aws_iam_role_policy_attachment" "ebs_csi" {
  count = var.enable_eks ? 1 : 0

  role       = aws_iam_role.ebs_csi[0].name
  policy_arn = "arn:${data.aws_partition.current.partition}:iam::aws:policy/service-role/AmazonEBSCSIDriverPolicy"
}

resource "terraform_data" "eks_cost_guard" {
  count = var.enable_eks ? 1 : 0

  input = var.acknowledge_eks_costs

  lifecycle {
    precondition {
      condition     = var.acknowledge_eks_costs
      error_message = "EKS has hourly control-plane, compute, storage, network, and IPv4 costs. Set acknowledge_eks_costs=true only for an intentional deployment window."
    }
  }
}

module "vpc" {
  count = var.enable_eks ? 1 : 0

  source  = "terraform-aws-modules/vpc/aws"
  version = "6.6.1"

  name = local.name
  cidr = var.vpc_cidr

  azs             = local.azs
  private_subnets = [for index, _ in local.azs : cidrsubnet(var.vpc_cidr, 4, index)]
  public_subnets  = [for index, _ in local.azs : cidrsubnet(var.vpc_cidr, 8, index + 48)]

  enable_dns_hostnames = true
  enable_dns_support   = true
  enable_nat_gateway   = true
  single_nat_gateway   = var.single_nat_gateway

  public_subnet_tags = {
    "kubernetes.io/role/elb" = 1
  }

  private_subnet_tags = {
    "kubernetes.io/role/internal-elb" = 1
  }

  tags = local.common_tags
}

module "eks" {
  count = var.enable_eks ? 1 : 0

  source  = "terraform-aws-modules/eks/aws"
  version = "21.24.0"

  name               = local.name
  kubernetes_version = var.kubernetes_version

  authentication_mode                      = "API_AND_CONFIG_MAP"
  enable_cluster_creator_admin_permissions = true
  deletion_protection                      = var.environment == "production"

  endpoint_private_access      = true
  endpoint_public_access       = var.cluster_endpoint_public_access
  endpoint_public_access_cidrs = var.cluster_endpoint_public_access_cidrs

  enabled_log_types                      = ["api", "audit", "authenticator", "controllerManager", "scheduler"]
  cloudwatch_log_group_retention_in_days = 30

  addons = {
    aws-ebs-csi-driver = {
      before_compute = true
      pod_identity_association = [{
        role_arn        = aws_iam_role.ebs_csi[0].arn
        service_account = "ebs-csi-controller-sa"
      }]
    }
    coredns = {}
    eks-pod-identity-agent = {
      before_compute = true
    }
    kube-proxy     = {}
    metrics-server = {}
    vpc-cni = {
      before_compute = true
    }
  }

  vpc_id     = module.vpc[0].vpc_id
  subnet_ids = module.vpc[0].private_subnets

  eks_managed_node_groups = {
    system = {
      ami_type       = "AL2023_x86_64_STANDARD"
      capacity_type  = "ON_DEMAND"
      instance_types = var.node_instance_types

      min_size     = var.node_min_size
      desired_size = var.node_desired_size
      max_size     = var.node_max_size

      update_config = {
        max_unavailable_percentage = 33
      }

      labels = {
        workload = "platform"
      }
    }
  }

  access_entries = var.enable_github_oidc ? {
    github_actions = {
      principal_arn = aws_iam_role.github_actions_eks[0].arn
      policy_associations = {
        namespace_admin = {
          policy_arn = "arn:${data.aws_partition.current.partition}:eks::aws:cluster-access-policy/AmazonEKSAdminPolicy"
          access_scope = {
            type       = "namespace"
            namespaces = [var.kubernetes_namespace]
          }
        }
      }
    }
  } : {}

  tags = local.common_tags

}

resource "aws_secretsmanager_secret" "runtime" {
  count = var.enable_eks ? 1 : 0

  name                    = "${local.name}/runtime"
  description             = "Runtime credentials consumed by the Conexao Solidaria EKS secret-sync workload."
  recovery_window_in_days = var.environment == "production" ? 7 : 0

  tags = local.common_tags
}

resource "aws_iam_role" "runtime_secret_reader" {
  count = var.enable_eks ? 1 : 0

  name               = "${local.name}-runtime-secret-reader"
  assume_role_policy = local.pod_identity_assume_role_policy

  tags = local.common_tags
}

resource "aws_iam_role_policy" "runtime_secret_reader" {
  count = var.enable_eks ? 1 : 0

  name = "read-runtime-secret"
  role = aws_iam_role.runtime_secret_reader[0].id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect = "Allow"
      Action = [
        "secretsmanager:DescribeSecret",
        "secretsmanager:GetSecretValue",
      ]
      Resource = aws_secretsmanager_secret.runtime[0].arn
    }]
  })
}

resource "aws_eks_pod_identity_association" "runtime_secret_sync" {
  count = var.enable_eks ? 1 : 0

  cluster_name    = module.eks[0].cluster_name
  namespace       = var.kubernetes_namespace
  service_account = "conexao-solidaria-secret-sync"
  role_arn        = aws_iam_role.runtime_secret_reader[0].arn
}

resource "aws_ecr_repository" "component" {
  for_each = local.components

  name                 = "conexao-solidaria-${each.key}"
  image_tag_mutability = "IMMUTABLE_WITH_EXCLUSION"

  image_tag_mutability_exclusion_filter {
    filter_type = "WILDCARD"
    filter      = "latest"
  }

  image_scanning_configuration {
    scan_on_push = true
  }

  encryption_configuration {
    encryption_type = "AES256"
  }

  tags = local.common_tags
}

resource "aws_ecr_lifecycle_policy" "component" {
  for_each = aws_ecr_repository.component

  repository = each.value.name
  policy = jsonencode({
    rules = [
      {
        rulePriority = 1
        description  = "Expire untagged images after seven days"
        selection = {
          tagStatus   = "untagged"
          countType   = "sinceImagePushed"
          countUnit   = "days"
          countNumber = 7
        }
        action = {
          type = "expire"
        }
      },
      {
        rulePriority = 2
        description  = "Retain the most recent tagged images"
        selection = {
          tagStatus      = "tagged"
          tagPatternList = ["*"]
          countType      = "imageCountMoreThan"
          countNumber    = var.ecr_image_retention_count
        }
        action = {
          type = "expire"
        }
      },
    ]
  })
}

resource "aws_iam_openid_connect_provider" "github" {
  count = var.enable_github_oidc ? 1 : 0

  url            = "https://token.actions.githubusercontent.com"
  client_id_list = ["sts.amazonaws.com"]

  tags = local.common_tags
}

resource "aws_iam_role" "github_actions_ecr" {
  count = var.enable_github_oidc ? 1 : 0

  name                 = "conexao-solidaria-github-ecr-${var.environment}"
  max_session_duration = 3600

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect = "Allow"
      Principal = {
        Federated = aws_iam_openid_connect_provider.github[0].arn
      }
      Action = "sts:AssumeRoleWithWebIdentity"
      Condition = {
        StringEquals = {
          "token.actions.githubusercontent.com:aud" = "sts.amazonaws.com"
          "token.actions.githubusercontent.com:sub" = local.github_main_subject
        }
      }
    }]
  })

  tags = local.common_tags
}

resource "aws_iam_role_policy" "github_actions_ecr" {
  count = var.enable_github_oidc ? 1 : 0

  name = "publish-component-images"
  role = aws_iam_role.github_actions_ecr[0].id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Sid      = "AuthenticateToEcr"
        Effect   = "Allow"
        Action   = "ecr:GetAuthorizationToken"
        Resource = "*"
      },
      {
        Sid    = "PublishComponentImages"
        Effect = "Allow"
        Action = [
          "ecr:BatchCheckLayerAvailability",
          "ecr:BatchGetImage",
          "ecr:CompleteLayerUpload",
          "ecr:DescribeImages",
          "ecr:GetDownloadUrlForLayer",
          "ecr:InitiateLayerUpload",
          "ecr:PutImage",
          "ecr:UploadLayerPart",
        ]
        Resource = values(aws_ecr_repository.component)[*].arn
      },
    ]
  })
}

resource "aws_iam_role" "github_actions_eks" {
  count = var.enable_eks && var.enable_github_oidc ? 1 : 0

  name                 = "conexao-solidaria-github-eks-${var.environment}"
  max_session_duration = 3600

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect = "Allow"
      Principal = {
        Federated = aws_iam_openid_connect_provider.github[0].arn
      }
      Action = "sts:AssumeRoleWithWebIdentity"
      Condition = {
        StringEquals = {
          "token.actions.githubusercontent.com:aud" = "sts.amazonaws.com"
          "token.actions.githubusercontent.com:sub" = local.github_deployment_subject
        }
      }
    }]
  })

  tags = local.common_tags
}

resource "aws_iam_role_policy" "github_actions_eks" {
  count = var.enable_eks && var.enable_github_oidc ? 1 : 0

  name = "describe-target-cluster"
  role = aws_iam_role.github_actions_eks[0].id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect   = "Allow"
      Action   = "eks:DescribeCluster"
      Resource = local.cluster_arn
    }]
  })
}
