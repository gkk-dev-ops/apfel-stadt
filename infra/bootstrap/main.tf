terraform {
  required_version = ">= 1.9.0, < 2.0.0"
  required_providers {
    google = {
      source  = "hashicorp/google"
      version = "~> 7.0"
    }
  }
}
provider "google" { project = var.project_id }
variable "project_id" { type = string }
variable "region" {
  type    = string
  default = "europe-west1"
}
variable "state_bucket_name" { type = string }
resource "google_project_service" "storage" {
  project            = var.project_id
  service            = "storage.googleapis.com"
  disable_on_destroy = false
}
resource "google_storage_bucket" "state" {
  name                        = var.state_bucket_name
  project                     = var.project_id
  location                    = var.region
  uniform_bucket_level_access = true
  public_access_prevention    = "enforced"
  force_destroy               = false
  versioning { enabled = true }
  soft_delete_policy { retention_duration_seconds = 604800 }
  lifecycle { prevent_destroy = true }
  depends_on = [google_project_service.storage]
}
output "state_bucket" { value = google_storage_bucket.state.name }
