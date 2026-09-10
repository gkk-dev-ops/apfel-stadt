variable "project_id" { type = string }
variable "region" {
  type    = string
  default = "europe-west1"
}
variable "environment" {
  type    = string
  default = "dev"
  validation {
    condition     = contains(["dev", "prod"], var.environment)
    error_message = "Use separate GCP projects for dev and prod."
  }
}
variable "enable_api" {
  type    = bool
  default = false
}
variable "api_image" {
  type    = string
  default = ""
  validation {
    condition     = var.api_image == "" || can(regex("@sha256:[a-f0-9]{64}$", var.api_image))
    error_message = "Use an immutable container image digest, not a mutable tag."
  }
}
variable "allowed_user_uids" {
  type    = list(string)
  default = []
}
variable "billing_account_id" {
  type    = string
  default = ""
}
variable "budget_amount" {
  type    = number
  default = 10
  validation {
    condition     = var.budget_amount > 0 && floor(var.budget_amount) == var.budget_amount
    error_message = "Budget amount must be a positive whole currency unit."
  }
}
variable "budget_currency" {
  type    = string
  default = "EUR"
}
variable "notification_channels" {
  type        = list(string)
  default     = []
  description = "Existing Monitoring channel resource names; no new recipients created."
}
variable "github_repository_id" {
  type    = string
  default = ""
  validation {
    condition     = var.github_repository_id == "" || can(regex("^[0-9]+$", var.github_repository_id))
    error_message = "Use the numeric GitHub ID, not its name."
  }
}
variable "github_owner_id" {
  type    = string
  default = ""
  validation {
    condition     = var.github_owner_id == "" || can(regex("^[0-9]+$", var.github_owner_id))
    error_message = "Use the numeric GitHub ID, not its name."
  }
}
