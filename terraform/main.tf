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

variable "github_token" {
  description = "GitHub Personal Access Token for repository access"
  type        = string
  sensitive   = true
  default     = null
}

resource "random_string" "suffix" {
  length  = 6
  special = false
  upper   = false
}

resource "azurerm_resource_group" "jarvis" {
  name     = "rg-jarvis-s0id0v"
  location = "East US"
}

# Azure SQL Database
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
}

resource "azurerm_mssql_database" "suit_db" {
  name      = "SuitTelemetryDB"
  server_id = azurerm_mssql_server.jarvis.id
  sku_name  = "Basic"
}

# Allow Azure services to access SQL
resource "azurerm_mssql_firewall_rule" "azure" {
  name             = "AllowAzureServices"
  server_id        = azurerm_mssql_server.jarvis.id
  start_ip_address = "0.0.0.0"
  end_ip_address   = "0.0.0.0"
}

# Redis Cache
resource "azurerm_redis_cache" "jarvis" {
  name                = "redis-jarvis-${random_string.suffix.result}"
  resource_group_name = azurerm_resource_group.jarvis.name
  location            = azurerm_resource_group.jarvis.location
  capacity            = 1
  family              = "C"
  sku_name            = "Basic"
}

# Service Bus
resource "azurerm_servicebus_namespace" "jarvis" {
  name                = "sb-jarvis-${random_string.suffix.result}"
  resource_group_name = azurerm_resource_group.jarvis.name
  location            = azurerm_resource_group.jarvis.location
  sku                 = "Standard"
}

resource "azurerm_servicebus_queue" "suit_events" {
  name         = "suit-events"
  namespace_id = azurerm_servicebus_namespace.jarvis.id
}

# ===== STATIC WEB APP - NO VM QUOTA NEEDED =====
resource "azurerm_static_web_app" "jarvis" {
  name                = "swa-jarvis-${random_string.suffix.result}"
  resource_group_name = azurerm_resource_group.jarvis.name
  location            = "eastus2"  # Static Web Apps only work in specific regions
  
  app_settings = {
    "ConnectionStrings__SuitDatabase" = "Server=tcp:${azurerm_mssql_server.jarvis.fully_qualified_domain_name},1433;Initial Catalog=SuitTelemetryDB;User ID=${azurerm_mssql_server.jarvis.administrator_login};Password=${random_password.sql_password.result};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
    "ConnectionStrings__Redis"         = "${azurerm_redis_cache.jarvis.hostname}:${azurerm_redis_cache.jarvis.ssl_port},password=${azurerm_redis_cache.jarvis.primary_access_key},ssl=True,abortConnect=False"
    "ConnectionStrings__ServiceBus"    = azurerm_servicebus_namespace.jarvis.default_primary_connection_string
    "ASPNETCORE_ENVIRONMENT"           = "Production"
  }
  
  tags = {
    Project     = "JARVIS Suit Brain"
    Environment = "Production"
  }
}

# ===== OUTPUTS =====
output "static_web_app_url" {
  value = "https://${azurerm_static_web_app.jarvis.default_host_name}"
  description = "The URL of the deployed JARVIS API"
}

output "static_web_app_api_key" {
  value     = azurerm_static_web_app.jarvis.api_key
  sensitive = true
  description = "API key for the Static Web App"
}

output "resource_group" {
  value = azurerm_resource_group.jarvis.name
  description = "Resource group name"
}

output "sql_server_name" {
  value = azurerm_mssql_server.jarvis.name
  description = "SQL Server name"
}

output "redis_hostname" {
  value = azurerm_redis_cache.jarvis.hostname
  description = "Redis cache hostname"
}

output "servicebus_namespace" {
  value = azurerm_servicebus_namespace.jarvis.name
  description = "Service Bus namespace"
}