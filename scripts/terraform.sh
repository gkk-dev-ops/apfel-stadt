#!/usr/bin/env bash
source "$(dirname "$0")/common.sh"
need terraform
export CHECKPOINT_DISABLE=1
stack="${1:-}"
action="${2:-}"
case "$stack" in bootstrap|environment) ;; *) die 'Stack must be bootstrap or environment.' ;; esac
case "$action" in init|validate|plan) ;; *) die 'Action must be init, validate or plan. Apply a reviewed plan explicitly with Terraform.' ;; esac
dir="$TOWN_ROOT/infra/$stack"
if [[ "$action" == init ]]; then
  if [[ "$stack" == environment ]]; then
    [[ -n "${TF_STATE_BUCKET:-}" && -n "${TF_STATE_PREFIX:-}" ]] || die 'Set TF_STATE_BUCKET and TF_STATE_PREFIX (e.g. town/dev).'
    terraform -chdir="$dir" init -input=false \
      "-backend-config=bucket=$TF_STATE_BUCKET" "-backend-config=prefix=$TF_STATE_PREFIX"
  else
    terraform -chdir="$dir" init -input=false
  fi
elif [[ "$action" == validate ]]; then
  terraform -chdir="$dir" fmt -check
  terraform -chdir="$dir" validate
else
  [[ -f "$dir/terraform.tfvars" ]] || die "Copy $dir/terraform.tfvars.example to terraform.tfvars and fill it in."
  terraform -chdir="$dir" plan -input=false -out=review.tfplan
fi
