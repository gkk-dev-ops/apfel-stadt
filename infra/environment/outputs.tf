output "api_url" {
  value = var.enable_api ? google_cloud_run_v2_service.api[0].uri : null
}
output "worlds_bucket" { value = google_storage_bucket.worlds.name }
output "build_source_bucket" { value = google_storage_bucket.build_source.name }
output "builder_service_account" { value = google_service_account.builder.email }
output "registry_image_base" {
  value = "${var.region}-docker.pkg.dev/${var.project_id}/${google_artifact_registry_repository.api.repository_id}/sync-api"
}
output "auth_api_key" {
  value     = google_apikeys_key.auth_client.key_string
  sensitive = true
}
output "github_provider" {
  value = local.enable_github ? google_iam_workload_identity_pool_provider.github[0].name : null
}
