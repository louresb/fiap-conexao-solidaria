terraform {
  required_version = ">= 1.10, < 2.0"

  backend "azurerm" {}

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 5.0"
    }
  }
}

provider "azurerm" {
  resource_provider_registrations = "none"
  features {}
}
