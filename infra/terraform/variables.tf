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

variable "key_vault_name" {
  description = "The name of the Key Vault"
  default     = "keyvault-pcds"
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
