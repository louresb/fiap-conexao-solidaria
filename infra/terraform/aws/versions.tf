terraform {
  required_version = ">= 1.10, < 2.0"

  backend "s3" {}

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
  }
}

provider "aws" {
  region = var.aws_region

  allowed_account_ids = var.expected_aws_account_id == null ? null : [var.expected_aws_account_id]

  default_tags {
    tags = local.common_tags
  }
}
