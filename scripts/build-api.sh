#!/usr/bin/env bash
source "$(dirname "$0")/common.sh"
need gcloud
need terraform
need git
need python3
export CHECKPOINT_DISABLE=1
cd "$TOWN_ROOT"
# A clean commit makes the image tag traceable to reviewed source.
[[ -z "$(git status --porcelain)" ]] || die 'Commit reviewed source before building a tagged API image.'
project="$(terraform -chdir=infra/environment output -json | python3 -c 'import json,sys; x=json.load(sys.stdin); print(x["registry_image_base"]["value"].split("/")[1])')"
image="$(terraform -chdir=infra/environment output -raw registry_image_base)"
bucket="$(terraform -chdir=infra/environment output -raw build_source_bucket)"
builder="$(terraform -chdir=infra/environment output -raw builder_service_account)"
region="${image%%-docker.pkg.dev/*}"
tag="$(git rev-parse HEAD)"
gcloud builds submit . --project="$project" --region="$region" \
  --config=cloudbuild.yaml --service-account="projects/$project/serviceAccounts/$builder" \
  --gcs-source-staging-dir="gs://$bucket/source" \
  --substitutions="_IMAGE=$image,_TAG=$tag"
digest="$(gcloud artifacts docker images describe "$image:$tag" --project="$project" --format='value(image_summary.digest)')"
[[ "$digest" == sha256:* ]] || die 'Build completed but no digest was resolved.'
printf 'Reviewed Terraform api_image value: %s@%s\n' "$image" "$digest"
