terraform {
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~>3.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~>3.0"
    }
  }
}

provider "azurerm" {
  features {}
}

resource "random_string" "suffix" {
  length  = 6
  special = false
  upper   = false
}

resource "azurerm_resource_group" "jarvis" {
  name     = "rg-jarvis-${random_string.suffix.result}"
  location = "East US"
  tags = {
    Project = "JARVIS Suit Brain"
    IaC     = "Terraform"
  }
}

# ===== BACKING SERVICE 1: Azure SQL Database =====
resource "azurerm_mssql_server" "jarvis" {
  name                         = "sql-jarvis-${random_string.suffix.result}"
  resource_group_name          = azurerm_resource_group.jarvis.name
  location                     = azurerm_resource_group.jarvis.location
  version                      = "12.0"
  administrator_login          = "jarvisadmin"
  administrator_login_password = random_password.sql_password.result
}

resource "random_password" "sql_password" {
  length  = 20
  special = false
  min_lower = 1
  min_upper = 1
  min_numeric = 1
}

resource "azurerm_mssql_database" "suit_db" {
  name      = "SuitTelemetryDB"
  server_id = azurerm_mssql_server.jarvis.id
  sku_name  = "Basic"
}

resource "azurerm_mssql_firewall_rule" "azure_services" {
  name             = "AllowAzureServices"
  server_id        = azurerm_mssql_server.jarvis.id
  start_ip_address = "0.0.0.0"
  end_ip_address   = "0.0.0.0"
}

# ===== BACKING SERVICE 2: Azure Redis Cache =====
resource "azurerm_redis_cache" "jarvis" {
  name                = "redis-jarvis-${random_string.suffix.result}"
  resource_group_name = azurerm_resource_group.jarvis.name
  location            = azurerm_resource_group.jarvis.location
  capacity            = 1
  family              = "C"
  sku_name            = "Basic"
}

# ===== BACKING SERVICE 3: Azure Service Bus =====
resource "azurerm_servicebus_namespace" "jarvis" {
  name                = "sb-jarvis-${random_string.suffix.result}"
  resource_group_name = azurerm_resource_group.jarvis.name
  location            = azurerm_resource_group.jarvis.location
  sku                 = "Standard"
}

resource "azurerm_servicebus_queue" "suit_events" {
  name         = "suit-events"
  namespace_id = azurerm_servicebus_namespace.jarvis.id
  
  requires_duplicate_detection = true
  duplicate_detection_history_time_window = "PT10M"
}

# ===== App Service Plan & App Service =====
resource "azurerm_service_plan" "jarvis" {
  name                = "asp-jarvis-${random_string.suffix.result}"
  resource_group_name = azurerm_resource_group.jarvis.name
  location            = azurerm_resource_group.jarvis.location
  os_type             = "Linux"
  sku_name            = "F1"  # Free tier - NO VM quota needed
}

resource "azurerm_linux_web_app" "jarvis_api" {
  name                = "app-jarvis-${random_string.suffix.result}"
  resource_group_name = azurerm_resource_group.jarvis.name
  location            = azurerm_resource_group.jarvis.location
  service_plan_id     = azurerm_service_plan.jarvis.id

  site_config {
    application_stack {
      dotnet_version = "8.0"
    }
  }

  app_settings = {
    "ConnectionStrings__SuitDatabase" = "Server=tcp:${azurerm_mssql_server.jarvis.fully_qualified_domain_name},1433;Initial Catalog=SuitTelemetryDB;User ID=${azurerm_mssql_server.jarvis.administrator_login};Password=${random_password.sql_password.result};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
    "ConnectionStrings__Redis"         = "${azurerm_redis_cache.jarvis.hostname}:${azurerm_redis_cache.jarvis.ssl_port},password=${azurerm_redis_cache.jarvis.primary_access_key},ssl=True,abortConnect=False"
    "ConnectionStrings__ServiceBus"    = azurerm_servicebus_namespace.jarvis.default_primary_connection_string
    "ASPNETCORE_ENVIRONMENT"           = "Production"
  }
}

output "app_url" {
  value = "https://${azurerm_linux_web_app.jarvis_api.default_hostname}"
}

output "resource_group" {
  value = azurerm_resource_group.jarvis.name
}