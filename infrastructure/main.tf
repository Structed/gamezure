# Configure the Azure provider
terraform {
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = ">= 2.48"
    }
  }

  backend "azurerm" {}
}

provider "azurerm" {
  features {}
}

locals {
  management_prefix = "${var.prefix}-management"
  vmpool_prefix     = "${var.prefix}-vmpool"
}

resource "azurerm_resource_group" "rg_management" {
  name     = "${local.management_prefix}-rg"
  location = var.location
}

resource "azurerm_resource_group" "rg_vmpool" {
  name     = "${local.vmpool_prefix}-rg"
  location = var.location
}

resource "azurerm_role_assignment" "contributor_role_assignment" {
  scope                = azurerm_resource_group.rg_vmpool.id
  role_definition_name = "Contributor"
  principal_id         = var.sp_client_id
}

resource "azurerm_storage_account" "storage" {
  name                     = replace("${var.prefix}storage", "-", "")
  resource_group_name      = azurerm_resource_group.rg_management.name
  location                 = azurerm_resource_group.rg_management.location
  account_kind             = "StorageV2"
  account_tier             = "Standard"
  account_replication_type = "LRS"
  access_tier              = "Hot"
}

module "cosmosdb" {
  source         = "./cosmosdb"
  resource_group = azurerm_resource_group.rg_management
  tags           = var.tags
}


resource "azurerm_app_service_plan" "app_service_plan" {
  name                = "${var.prefix}-app-service-plan"
  location            = azurerm_resource_group.rg_management.location
  resource_group_name = azurerm_resource_group.rg_management.name
  kind                = "FunctionApp"

  sku {
    tier = "Dynamic"
    size = "Y1"
  }
}

resource "azurerm_function_app" "function" {
  name                       = "${var.prefix}-function"
  location                   = azurerm_resource_group.rg_management.location
  resource_group_name        = azurerm_resource_group.rg_management.name
  app_service_plan_id        = azurerm_app_service_plan.app_service_plan.id
  storage_account_name       = azurerm_storage_account.storage.name
  storage_account_access_key = azurerm_storage_account.storage.primary_access_key

  enabled                = true
  enable_builtin_logging = true
  https_only             = true
  version                 = "~3"
//  identity {
//    type = ""
//  }

  connection_string {
    name  = "CosmosDb"
    type  = "Custom"
    value = module.cosmosdb.connection_strings[0]
  }

  site_config {
    ftps_state = "Disabled"
    //    ip_restriction = []
    min_tls_version = "1.2"
  }
}

