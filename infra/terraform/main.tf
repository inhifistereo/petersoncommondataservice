# Configure the Azure provider
terraform {
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.20.0"
    }
  }
  backend "azurerm" {
    resource_group_name  = "terraform-rg"
    storage_account_name = "dptfstate1983"
    container_name       = "tfstate"
    key                  = "terraform.tfstate"
  }
}

provider "azurerm" {
  features {}
  subscription_id = var.subscription_id
  tenant_id       = var.tenant_id
}

locals {
  tags = {
    application = "petersoncommondataservice"
    environment = "production"
    managed-by  = "terraform"
    repository  = "inhifistereo/petersoncommondataservice"
  }
}

resource "azurerm_resource_group" "rg" {
  name     = var.resource_group_name
  location = var.location
  tags     = local.tags
}

resource "azurerm_log_analytics_workspace" "log_analytics" {
  name                = var.log_analytics_workspace_name
  location            = var.location
  resource_group_name = azurerm_resource_group.rg.name
  sku                 = "PerGB2018"
  retention_in_days   = 30

  # A runaway log loop on a pay-per-GB workspace is the one way this deployment can get
  # expensive. One replica of an idle API writes a few MB a day, so this cap is far above
  # normal traffic and only trips on a genuine fault. Ingestion stops for the rest of the
  # UTC day when it does, so raise it rather than debug blind if logs ever go missing.
  daily_quota_gb = var.log_analytics_daily_quota_gb

  tags = local.tags
}

# Deliberately untagged, unlike every other resource here. Any update to a container app
# environment re-sends a full CreateOrUpdate, and its LogAnalyticsConfiguration has to
# carry the workspace shared key - which the Azure API returns as null on every read. So
# the provider faithfully sends that null back and Azure rejects the whole call with
# "LogAnalyticsConfiguration is invalid". Adding four tags was enough to trigger it and
# leave an apply half-finished. Four tags are not worth an unrunnable pipeline.
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
  tags                = local.tags
}

resource "azurerm_container_app" "app" {
  name                         = var.container_app_name
  resource_group_name          = azurerm_resource_group.rg.name
  container_app_environment_id = azurerm_container_app_environment.env.id
  revision_mode                = "Single"
  tags                         = local.tags

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

  secret {
    name  = "api-keys-secret"
    value = var.api_keys
  }

  # Weather is optional. These blocks are omitted entirely when no coordinates are
  # supplied, because Azure rejects a Container App secret with an empty value - and the
  # app already degrades to a 503 on /weather when the location is unset.
  dynamic "secret" {
    for_each = var.weather_latitude != "" && var.weather_longitude != "" ? [1] : []
    content {
      name  = "weather-latitude-secret"
      value = var.weather_latitude
    }
  }

  dynamic "secret" {
    for_each = var.weather_latitude != "" && var.weather_longitude != "" ? [1] : []
    content {
      name  = "weather-longitude-secret"
      value = var.weather_longitude
    }
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
    # min_replicas is left at the platform default of 0 deliberately. The wall display
    # polls every couple of minutes, well inside the ~5 minute idle window, so a replica
    # stays alive on its own and the in-process cache survives between polls. Paying for
    # an always-on replica would buy nothing the refresh cadence does not already provide.

    container {
      name  = "petersoncommondataservice"
      image = "${var.container_registry_name}.azurecr.io/petersoncommondataservice:${var.image_tag}"

      # This service is I/O-bound and idle almost all the time. Container Apps only
      # permits specific CPU/memory pairs; 0.25 vCPU must pair with 0.5Gi.
      cpu    = 0.25
      memory = "0.5Gi"

      # Both probes point at /health/live, which runs zero checks and only proves the
      # process answers. Pointing them at anything that touches an upstream would let a
      # Todoist outage kill a healthy container and destroy the cache that exists
      # precisely to survive that outage. Upstream health is reported by /health/ready
      # and by the "stale" flag in each response envelope, neither of which is wired to
      # a probe.
      liveness_probe {
        transport = "HTTP"
        port      = 8080
        path      = "/health/live"

        initial_delay           = 30
        interval_seconds        = 10
        timeout                 = 5
        failure_count_threshold = 3
      }

      startup_probe {
        transport = "HTTP"
        port      = 8080
        path      = "/health/live"

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
        name        = "Api__Keys"
        secret_name = "api-keys-secret"
      }

      dynamic "env" {
        for_each = var.weather_latitude != "" && var.weather_longitude != "" ? [1] : []
        content {
          name        = "Weather__Latitude"
          secret_name = "weather-latitude-secret"
        }
      }

      dynamic "env" {
        for_each = var.weather_latitude != "" && var.weather_longitude != "" ? [1] : []
        content {
          name        = "Weather__Longitude"
          secret_name = "weather-longitude-secret"
        }
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

# The container app pulls its image as its own system-assigned identity
# (registry { identity = "System" } above), which only works while that identity holds
# AcrPull on the registry. That grant was made by hand and lived nowhere in this config,
# so nothing detected its removal and a rebuild from scratch produced an app that could
# never pull. Declaring it here makes the dependency visible and drift detectable.
#
# Two caveats worth knowing before relying on this for a from-scratch rebuild:
#   - The CI service principal is Contributor, which can read role assignments but cannot
#     create them. Importing works; creating one would need User Access Administrator.
#   - The grant depends on an identity that only exists once the app is created, but the
#     app needs the grant to pull its first image. Breaking that cycle means moving to a
#     user-assigned identity created ahead of the app.
resource "azurerm_role_assignment" "acr_pull" {
  scope                = azurerm_container_registry.acr.id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_container_app.app.identity[0].principal_id
}

# Transitional: adopts the existing hand-made assignment instead of trying to create a
# duplicate, which Azure rejects with RoleAssignmentExists. Leaving this in place is
# harmless on later runs - Terraform skips an import whose target is already in state -
# but delete it once the apply on main has succeeded, because an import block pointed at
# a resource that does not exist fails the plan, which is exactly the from-scratch case
# this resource was added to support.
import {
  to = azurerm_role_assignment.acr_pull
  id = "${local.acr_id}/providers/Microsoft.Authorization/roleAssignments/${var.acr_pull_role_assignment_id}"
}

locals {
  # Built from variables rather than referenced off azurerm_container_registry.acr.id
  # because an import block's id has to resolve at plan time, before any resource is read.
  acr_id = "/subscriptions/${var.subscription_id}/resourceGroups/${var.resource_group_name}/providers/Microsoft.ContainerRegistry/registries/${var.container_registry_name}"
}

resource "azurerm_container_app_custom_domain" "custom_domain" {
  container_app_id = azurerm_container_app.app.id
  name             = var.domain_name

  lifecycle {
    ignore_changes = [certificate_binding_type, container_app_environment_certificate_id]
  }
}

# The app's stable hostname. Deliberately not latest_revision_fqdn, which carries the
# revision name and therefore changes on every single deploy.
output "container_app_url" {
  value = "https://${azurerm_container_app.app.ingress[0].fqdn}"
}

output "custom_domain_url" {
  value = "https://${var.domain_name}"
}
