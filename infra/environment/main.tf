locals {
  prefix = "town-${var.environment}"
  services = toset([
    "run.googleapis.com", "firestore.googleapis.com", "storage.googleapis.com",
    "artifactregistry.googleapis.com", "identitytoolkit.googleapis.com",
    "securetoken.googleapis.com", "apikeys.googleapis.com", "iam.googleapis.com",
    "iamcredentials.googleapis.com", "sts.googleapis.com", "cloudbuild.googleapis.com",
    "logging.googleapis.com", "monitoring.googleapis.com", "billingbudgets.googleapis.com",
    "firebaserules.googleapis.com"
  ])
}
resource "google_project_service" "enabled" {
  for_each           = local.services
  project            = var.project_id
  service            = each.key
  disable_on_destroy = false
}
data "google_project" "current" { project_id = var.project_id }
resource "google_service_account" "api" {
  account_id   = "${local.prefix}-api"
  display_name = "Town runtime API"
  depends_on   = [google_project_service.enabled]
}
resource "google_service_account" "builder" {
  account_id   = "${local.prefix}-builder"
  display_name = "Town container builder; no infrastructure admin rights"
  depends_on   = [google_project_service.enabled]
}
resource "google_firestore_database" "worlds" {
  project                 = var.project_id
  name                    = "(default)"
  location_id             = var.region
  type                    = "FIRESTORE_NATIVE"
  concurrency_mode        = "PESSIMISTIC"
  delete_protection_state = "DELETE_PROTECTION_ENABLED"
  deletion_policy         = "ABANDON"
  lifecycle { prevent_destroy = true }
  depends_on = [google_project_service.enabled]
}
resource "google_firebaserules_ruleset" "deny_clients" {
  project = var.project_id
  source {
    files {
      name    = "firestore.rules"
      content = "rules_version = '2'; service cloud.firestore { match /databases/{database}/documents { match /{document=**} { allow read, write: if false; } } }"
    }
  }
  depends_on = [google_project_service.enabled, google_firestore_database.worlds]
}
resource "google_firebaserules_release" "firestore" {
  project      = var.project_id
  name         = "cloud.firestore"
  ruleset_name = google_firebaserules_ruleset.deny_clients.id
}
resource "google_storage_bucket" "worlds" {
  name                        = "${var.project_id}-${local.prefix}-worlds"
  location                    = var.region
  uniform_bucket_level_access = true
  public_access_prevention    = "enforced"
  force_destroy               = false
  soft_delete_policy { retention_duration_seconds = 604800 }
  # No age-based deletion: an inactive world's current save must survive.
  lifecycle { prevent_destroy = true }
  depends_on = [google_project_service.enabled]
}
resource "google_storage_bucket" "build_source" {
  name                        = "${var.project_id}-${local.prefix}-build-source"
  location                    = var.region
  uniform_bucket_level_access = true
  public_access_prevention    = "enforced"
  force_destroy               = false
  lifecycle_rule {
    condition { age = 14 }
    action { type = "Delete" }
  }
  depends_on = [google_project_service.enabled]
}
resource "google_artifact_registry_repository" "api" {
  location      = var.region
  repository_id = "${local.prefix}-api"
  format        = "DOCKER"
  depends_on    = [google_project_service.enabled]
}
resource "google_identity_platform_config" "auth" {
  project = var.project_id
  sign_in {
    allow_duplicate_emails = false
    email {
      enabled           = true
      password_required = true
    }
    anonymous { enabled = false }
    phone_number { enabled = false }
  }
  client {
    permissions {
      disabled_user_signup   = true
      disabled_user_deletion = true
    }
  }
  depends_on = [google_project_service.enabled]
}
resource "google_apikeys_key" "auth_client" {
  name         = "${local.prefix}-auth-client"
  display_name = "Public native-client auth API identifier"
  restrictions {
    api_targets { service = "identitytoolkit.googleapis.com" }
    api_targets { service = "securetoken.googleapis.com" }
  }
  depends_on = [google_project_service.enabled, google_identity_platform_config.auth]
}
resource "google_project_iam_member" "api_firestore" {
  role    = "roles/datastore.user"
  member  = "serviceAccount:${google_service_account.api.email}"
  project = var.project_id
}
resource "google_project_iam_member" "api_auth_read" {
  role    = "roles/firebaseauth.viewer"
  member  = "serviceAccount:${google_service_account.api.email}"
  project = var.project_id
}
resource "google_project_iam_custom_role" "snapshots" {
  role_id     = "town_${var.environment}_snapshots"
  title       = "Create and read immutable town snapshots"
  permissions = ["storage.objects.create", "storage.objects.get", "storage.objects.list"]
}
resource "google_storage_bucket_iam_member" "api_snapshots" {
  bucket = google_storage_bucket.worlds.name
  role   = google_project_iam_custom_role.snapshots.name
  member = "serviceAccount:${google_service_account.api.email}"
}
resource "google_artifact_registry_repository_iam_member" "builder" {
  location   = var.region
  repository = google_artifact_registry_repository.api.repository_id
  role       = "roles/artifactregistry.writer"
  member     = "serviceAccount:${google_service_account.builder.email}"
}
resource "google_storage_bucket_iam_member" "builder_source" {
  bucket = google_storage_bucket.build_source.name
  role   = "roles/storage.objectAdmin"
  member = "serviceAccount:${google_service_account.builder.email}"
}
resource "google_project_iam_member" "builder_logs" {
  project = var.project_id
  role    = "roles/logging.logWriter"
  member  = "serviceAccount:${google_service_account.builder.email}"
}
resource "google_project_iam_member" "builder_submit" {
  project = var.project_id
  role    = "roles/cloudbuild.builds.editor"
  member  = "serviceAccount:${google_service_account.builder.email}"
}
resource "google_service_account_iam_member" "builder_self_use" {
  service_account_id = google_service_account.builder.name
  role               = "roles/iam.serviceAccountUser"
  member             = "serviceAccount:${google_service_account.builder.email}"
}
resource "google_cloud_run_v2_service" "api" {
  count               = var.enable_api ? 1 : 0
  name                = "${local.prefix}-api"
  location            = var.region
  deletion_protection = true
  ingress             = "INGRESS_TRAFFIC_ALL"
  template {
    service_account                  = google_service_account.api.email
    timeout                          = "60s"
    max_instance_request_concurrency = 2
    scaling {
      min_instance_count = 0
      max_instance_count = 2
    }
    containers {
      image = var.api_image
      ports { container_port = 8080 }
      resources {
        limits            = { cpu = "1", memory = "512Mi" }
        cpu_idle          = true
        startup_cpu_boost = true
      }
      env {
        name  = "GOOGLE_CLOUD_PROJECT"
        value = var.project_id
      }
      env {
        name  = "WORLDS_BUCKET"
        value = google_storage_bucket.worlds.name
      }
      env {
        name  = "ALLOWED_USER_UIDS"
        value = jsonencode(var.allowed_user_uids)
      }
      startup_probe {
        initial_delay_seconds = 0
        period_seconds        = 3
        failure_threshold     = 10
        http_get { path = "/healthz" }
      }
    }
  }
  lifecycle {
    precondition {
      condition     = var.api_image != "" && length(var.allowed_user_uids) > 0
      error_message = "Enable API only with an image digest and explicit tester UIDs."
    }
  }
  depends_on = [google_project_service.enabled, google_project_iam_member.api_firestore, google_storage_bucket_iam_member.api_snapshots]
}
# Public transport only. API MUST verify application ID tokens + ownership itself.
resource "google_cloud_run_v2_service_iam_member" "client_transport" {
  count    = var.enable_api ? 1 : 0
  project  = var.project_id
  location = var.region
  name     = google_cloud_run_v2_service.api[0].name
  role     = "roles/run.invoker"
  member   = "allUsers"
}
resource "google_billing_budget" "town" {
  count           = var.billing_account_id != "" ? 1 : 0
  billing_account = var.billing_account_id
  display_name    = "${local.prefix} monthly budget (alert, not a spending cap)"
  budget_filter { projects = ["projects/${data.google_project.current.number}"] }
  amount {
    specified_amount {
      currency_code = var.budget_currency
      units         = tostring(var.budget_amount)
    }
  }
  threshold_rules { threshold_percent = 0.5 }
  threshold_rules { threshold_percent = 0.9 }
  threshold_rules { threshold_percent = 1.0 }
  depends_on = [google_project_service.enabled]
}
resource "google_monitoring_alert_policy" "api_errors" {
  count                 = var.enable_api ? 1 : 0
  display_name          = "${local.prefix} API 5xx"
  combiner              = "OR"
  notification_channels = var.notification_channels
  conditions {
    display_name = "At least 5 server errors over five minutes"
    condition_threshold {
      filter          = "resource.type = \"cloud_run_revision\" AND resource.label.service_name = \"${local.prefix}-api\" AND metric.type = \"run.googleapis.com/request_count\" AND metric.label.response_code_class = \"5xx\""
      duration        = "0s"
      comparison      = "COMPARISON_GT"
      threshold_value = 4
      aggregations {
        alignment_period     = "300s"
        per_series_aligner   = "ALIGN_SUM"
        cross_series_reducer = "REDUCE_SUM"
      }
    }
  }
  depends_on = [google_project_service.enabled]
}
