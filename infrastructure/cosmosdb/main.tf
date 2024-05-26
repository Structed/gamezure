resource "azurerm_cosmosdb_account" "cosmosdb_account" {
  name                          = "${var.prefix}-cosmosdb"
  location                      = var.resource_group.location
  resource_group_name           = var.resource_group.name
  offer_type                    = "standard"
  kind                          = "GlobalDocumentDB"
  enable_free_tier              = var.use_cosmosdb_free_tier
  analytical_storage_enabled    = false
  public_network_access_enabled = true

  capabilities {
    name = "EnableServerless"
  }

  consistency_policy {
    consistency_level = "Strong"
  }

  geo_location {
    location          = var.resource_group.location
    failover_priority = 0
  }

  tags = var.tags
}

resource "azurerm_cosmosdb_sql_database" "gamezure_db" {
  name                = "${var.prefix}-db"
  resource_group_name = azurerm_cosmosdb_account.cosmosdb_account.resource_group_name
  account_name        = azurerm_cosmosdb_account.cosmosdb_account.name
  # Must not set "throughput" for serverless
}

resource "azurerm_cosmosdb_sql_container" "gamezure_db_pool_container" {
  name                = "pool"
  resource_group_name = azurerm_cosmosdb_account.cosmosdb_account.resource_group_name
  account_name        = azurerm_cosmosdb_account.cosmosdb_account.name
  database_name       = azurerm_cosmosdb_sql_database.gamezure_db.name

  partition_key_path    = "/id"
  partition_key_version = 1
  # Must not set "throughput" for serverless

  indexing_policy {
    indexing_mode = "Consistent"

    included_path {
      path = "/*"
    }

    included_path {
      path = "/included/?"
    }

    excluded_path {
      path = "/excluded/?"
    }
  }

  #   unique_key {
  #     paths = ["/definition/idlong", "/definition/idshort"]
  #   }
}

resource "azurerm_cosmosdb_sql_container" "gamezure_db_vms_container" {
  name                = "vm"
  resource_group_name = azurerm_cosmosdb_account.cosmosdb_account.resource_group_name
  account_name        = azurerm_cosmosdb_account.cosmosdb_account.name
  database_name       = azurerm_cosmosdb_sql_database.gamezure_db.name

  partition_key_path    = "/poolId"
  partition_key_version = 1
  # Must not set "throughput" for serverless

  indexing_policy {
    indexing_mode = "Consistent"

    included_path {
      path = "/*"
    }

    included_path {
      path = "/included/?"
    }

    excluded_path {
      path = "/excluded/?"
    }
  }

  #   unique_key {
  #     paths = ["/definition/idlong", "/definition/idshort"]
  #   }
}
