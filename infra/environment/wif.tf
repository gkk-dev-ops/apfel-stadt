check "github_ids_together" {
  assert {
    condition     = (var.github_repository_id == "") == (var.github_owner_id == "")
    error_message = "Supply both GitHub repository and owner numeric IDs to enable federation."
  }
}
locals { enable_github = var.github_repository_id != "" && var.github_owner_id != "" }
resource "google_iam_workload_identity_pool" "github" {
  count                     = local.enable_github ? 1 : 0
  workload_identity_pool_id = "${local.prefix}-github"
  display_name              = "Town GitHub builds"
  depends_on                = [google_project_service.enabled]
}
resource "google_iam_workload_identity_pool_provider" "github" {
  count                              = local.enable_github ? 1 : 0
  workload_identity_pool_id          = google_iam_workload_identity_pool.github[0].workload_identity_pool_id
  workload_identity_pool_provider_id = "github"
  attribute_mapping = {
    "google.subject"                = "assertion.sub"
    "attribute.repository_id"       = "assertion.repository_id"
    "attribute.repository_owner_id" = "assertion.repository_owner_id"
  }
  attribute_condition = "assertion.repository_owner_id == '${var.github_owner_id}' && assertion.repository_id == '${var.github_repository_id}' && assertion.ref == 'refs/heads/main'"
  oidc { issuer_uri = "https://token.actions.githubusercontent.com" }
}
resource "google_service_account_iam_member" "github_builder" {
  count              = local.enable_github ? 1 : 0
  service_account_id = google_service_account.builder.name
  role               = "roles/iam.workloadIdentityUser"
  member             = "principalSet://iam.googleapis.com/${google_iam_workload_identity_pool.github[0].name}/attribute.repository_id/${var.github_repository_id}"
}
