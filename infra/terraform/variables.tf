variable "location" {
  description = "The Azure region to deploy resources"
  default     = "East US 2"
}

variable "resource_group_name" {
  description = "The name of the resource group"
  default     = "rg-petersoncommondataservice"
}

variable "log_analytics_workspace_name" {
  description = "The name of the Log Analytics workspace"
  default     = "logs-petersoncommondataservice"
}

variable "container_apps_environment_name" {
  description = "The name of the Container Apps environment"
  default     = "env-petersoncommondataservice"
}

variable "container_registry_name" {
  description = "The name of the Container Registry"
  default     = "acrpetersoncommondataservice"
}

variable "container_app_name" {
  description = "The name of the Container App"
  default     = "app-petersoncommondataservice"
}

variable "ics_url" {
  description = "ICS URL secret value"
  type        = string
  sensitive   = true
}

variable "todoist_api_key" {
  description = "Todoist API Key"
  type        = string
  sensitive   = true
}

variable "todoist_project_id" {
  description = "Todoist Project ID"
  type        = string
  sensitive   = true
}

variable "subscription_id" {
  description = "Azure Subscription ID"
  type        = string
}

variable "tenant_id" {
  description = "Azure Tenant ID"
  type        = string
}

variable "domain_name" {
  description = "Custom domain name for the application."
  type        = string
}

variable "image_tag" {
  description = "Container image tag to deploy. CI passes the commit SHA so every deploy produces a new revision."
  type        = string
  default     = "latest"
}

variable "api_keys" {
  description = "Comma-separated API keys accepted by the service. Required: the app refuses to start without one outside Development, so a missing value fails the plan rather than the running container."
  type        = string
  sensitive   = true
}

variable "weather_latitude" {
  description = "Latitude for the NWS forecast grid. Kept as a secret so a home location is not committed to the repo."
  type        = string
  sensitive   = true
  default     = ""
}

variable "weather_longitude" {
  description = "Longitude for the NWS forecast grid. Kept as a secret so a home location is not committed to the repo."
  type        = string
  sensitive   = true
  default     = ""
}

variable "log_analytics_daily_quota_gb" {
  description = "Ingestion cap for the Log Analytics workspace, in GB per UTC day. Guards against a runaway log loop on a pay-per-GB workspace; -1 disables the cap."
  type        = number
  default     = 1
}
