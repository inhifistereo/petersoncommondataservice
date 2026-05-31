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

  identity {
    type = "SystemAssigned"
  }

  secret {
    name  = "ics-url-secret"
    value = var.ics_url
  }

  secret {
    name  = "todoist-api-key-secret"
    value = var.todoist_api_key
  }

  secret {
    name  = "todoist-project-id-secret"
    value = var.todoist_project_id
  }

  ingress {
    external_enabled = true
    target_port      = 8080
    transport        = "auto"
    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  registry {
    server   = "${var.container_registry_name}.azurecr.io"
    identity = "System"
  }

  template {
    container {
      name   = "petersoncommondataservice"
      image  = "${var.container_registry_name}.azurecr.io/petersoncommondataservice:latest"
      cpu    = 0.5
      memory = "1Gi"

      liveness_probe {
        transport = "HTTP"
        port      = 8080
        path      = "/health"

        initial_delay           = 30
        interval_seconds        = 10
        timeout                 = 5
        failure_count_threshold = 3
      }

      startup_probe {
        transport = "HTTP"
        port      = 8080
        path      = "/health"

        initial_delay           = 15
        interval_seconds        = 5
        timeout                 = 5
        failure_count_threshold = 10
      }

      env {
        name        = "ICS-URL"
        secret_name = "ics-url-secret"
      }

      env {
        name        = "TODOIST-API-KEY"
        secret_name = "todoist-api-key-secret"
      }

      env {
        name        = "TODOIST-PROJECT-ID"
        secret_name = "todoist-project-id-secret"
      }

      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = "Production"
      }

      env {
        name  = "ASPNETCORE_HTTP_PORTS"
        value = "8080"
      }
    }
  }
}

resource "azurerm_container_app_custom_domain" "custom_domain" {
  container_app_id = azurerm_container_app.app.id
  name             = var.domain_name

  lifecycle {
    ignore_changes = [certificate_binding_type, container_app_environment_certificate_id]
  }
}

# Output the Container App URL
output "container_app_url" {
  value = azurerm_container_app.app.latest_revision_fqdn
}
