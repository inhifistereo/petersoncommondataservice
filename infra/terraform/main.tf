# Configure the Azure provider
terraform {
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.20.0"
    }
  }
  backend "azurerm" {
    resource_group_name   = "terraform-rg"
    storage_account_name  = "dptfstate1983"
    container_name        = "tfstate"
    key                   = "terraform.tfstate"
  }
}

provider "azurerm" {
  features {}
  subscription_id = var.subscription_id
  tenant_id       = var.tenant_id
}

resource "azurerm_resource_group" "rg" {
  name     = var.resource_group_name
  location = var.location
}

resource "azurerm_log_analytics_workspace" "log_analytics" {
  name                = var.log_analytics_workspace_name
  location            = var.location
  resource_group_name = azurerm_resource_group.rg.name
  sku                 = "PerGB2018"
  retention_in_days   = 30
}

resource "azurerm_container_app_environment" "env" {
  name                       = var.container_apps_environment_name
  location                   = var.location
  resource_group_name        = azurerm_resource_group.rg.name
  log_analytics_workspace_id = azurerm_log_analytics_workspace.log_analytics.id

}

resource "azurerm_key_vault" "kv" {
  name                = var.key_vault_name
  location            = var.location
  resource_group_name = azurerm_resource_group.rg.name
  sku_name            = "standard"

  enable_rbac_authorization   = true

  tenant_id                = data.azurerm_client_config.current.tenant_id
  purge_protection_enabled = false
}

# More secure: Disable ACR admin credentials
resource "azurerm_container_registry" "acr" {
  name                = var.container_registry_name
  location            = var.location
  resource_group_name = azurerm_resource_group.rg.name
  sku                 = "Basic"
  admin_enabled       = false
}

resource "azurerm_container_app" "app" {
  name                         = var.container_app_name
  resource_group_name          = azurerm_resource_group.rg.name
  container_app_environment_id = azurerm_container_app_environment.env.id
  revision_mode                = "Single"
  
  # Enable SystemAssigned Managed Identity
  identity {
    type = "SystemAssigned"
  }
  
  # Define the secrets
  secret {
    name  = "ics-url-secret"
    value = azurerm_key_vault_secret.ics_url.value
  }
  
  secret {
    name  = "todoist-api-key-secret"
    value = azurerm_key_vault_secret.todoist_api_key.value
  }
  
  secret {
    name  = "todoist-project-id-secret"
    value = azurerm_key_vault_secret.todoist_project_id.value
  }

  ingress {
    external_enabled = true
    target_port      = 80
    transport        = "auto"
    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  registry {
  server   = "${var.container_registry_name}.azurecr.io"
  identity = "System"  # Uses the system-assigned managed identity
  }

  template {
    container {
      name   = "petersoncommondataservice"
      image  = "${var.container_registry_name}.azurecr.io/petersoncommondataservice:latest"
      cpu    = 0.5
      memory = "1Gi"
      
      # Map secrets to environment variables
      env {
        name        = "ICS_URL"
        secret_name = "ics-url-secret"
      }
      
      env {
        name        = "TODOIST_API_KEY"
        secret_name = "todoist-api-key-secret"
      }
      
      env {
        name        = "TODOIST_PROJECT_ID"
        secret_name = "todoist-project-id-secret"
      }

      # Add Kestrel/ASP.NET Core configuration
      env {
        name  = "DOTNET_URLS"
        value = "http://+:80"
      }
      
      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = "Production"
      }
    }
  }
}

# Assign Key Vault Secrets User role to the Container App's Managed Identity
resource "azurerm_role_assignment" "kv_secrets_user" {
  principal_id         = azurerm_container_app.app.identity[0].principal_id
  role_definition_name = "Key Vault Secrets User"
  scope                = azurerm_key_vault.kv.id
}

resource "azurerm_key_vault_secret" "ics_url" {
  name         = "ICS-URL"
  value        = var.ics_url
  key_vault_id = azurerm_key_vault.kv.id
}

resource "azurerm_key_vault_secret" "todoist_api_key" {
  name         = "TODOIST-API-KEY"
  value        = var.todoist_api_key
  key_vault_id = azurerm_key_vault.kv.id
}

resource "azurerm_key_vault_secret" "todoist_project_id" {
  name         = "TODOIST-PROJECT-ID"
  value        = var.todoist_project_id
  key_vault_id = azurerm_key_vault.kv.id
}

resource "azurerm_container_app_custom_domain" "custom_domain" {
  container_app_id = azurerm_container_app.app.id
  name = var.domain_name

  lifecycle {
    # When using an Azure created Managed Certificate these values must be added to ignore_changes
    ignore_changes = [certificate_binding_type, container_app_environment_certificate_id]
  }
}

# Get Azure Client Config
data "azurerm_client_config" "current" {}

# Output the Container App URL
output "container_app_url" {
  value = azurerm_container_app.app.latest_revision_fqdn
}