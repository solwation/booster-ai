#!/usr/bin/env bash
# One-time setup of release code signing (#130, docs/DEVELOPMENT.md#code-signing), run from WSL by
# someone who may create Entra app registrations and assign roles on the Artifact Signing account.
#   tools/setup-signing.sh --dry-run   # only discovers and prints the plan; changes nothing
#   tools/setup-signing.sh             # asks before each change
# Uses the Windows Azure CLI (az, already signed in) and the WSL GitHub CLI (gh, signed in with admin
# rights on the repository). No secret is created: GitHub signs in through a federated credential (OIDC).
# Override discovery with SIGNING_ACCOUNT_ID=/subscriptions/.../codeSigningAccounts/<name> and
# SIGNING_PROFILE=<profile name>; APP_NAME sets the app registration's name.
set -euo pipefail

REPO=solwation/booster-ai
ENVIRONMENT=signing
APP_NAME="${APP_NAME:-booster-ai-github-signing}"
SUBJECT="repo:$REPO:environment:$ENVIRONMENT"
DRY=0
[ "${1:-}" = --dry-run ] && DRY=1

# The Windows az.cmd runs through cmd.exe: pass only plain tokens (no spaces, quotes, parentheses)
# and filter its JSON with jq here.
cd /mnt/c
# Errors print nothing; callers check the values they need.
az() { cmd.exe /d /c az "$@" 2>/dev/null | tr -d '\r' || true; }
die() { echo "error: $*" >&2; exit 1; }
ask() {
  [ "$DRY" = 1 ] && { echo "  (dry run: would $1)"; return 1; }
  read -r -p "  $1? [y/N] " a; [ "$a" = y ] || [ "$a" = Y ]
}

command -v jq >/dev/null || die "jq is missing (sudo apt install jq)"
command -v cmd.exe >/dev/null || die "run this from WSL with the Windows Azure CLI installed"
gh auth status >/dev/null 2>&1 || die "gh is not signed in (gh auth login)"
account=$(az account show -o json) || true
[ -n "$account" ] || die "az is not signed in (az login in Windows)"
echo "Azure: signed in as $(jq -r .user.name <<<"$account")"

# 1. Artifact Signing account
if [ -n "${SIGNING_ACCOUNT_ID:-}" ]; then
  ids=("$SIGNING_ACCOUNT_ID")
else
  echo "Looking for Artifact Signing accounts in every subscription (may take a minute)..."
  ids=()
  for sub in $(az account list -o json | jq -r '.[] | select(.state == "Enabled") | .id'); do
    while IFS= read -r id; do [ -n "$id" ] && ids+=("$id"); done < <(
      az resource list --subscription "$sub" --resource-type Microsoft.CodeSigning/codeSigningAccounts -o json | jq -r '.[].id')
  done
fi
if [ "${#ids[@]}" -eq 0 ]; then
  echo "No Artifact Signing account visible to this az login (wrong tenant: az login --tenant <id>; or no read access)."
  echo "Paste the account's resource ID (Azure portal: the account → JSON View → Resource ID), or leave empty to stop:"
  read -r id; [ -n "$id" ] || die "no signing account"
  ids=("$id")
fi
if [ "${#ids[@]}" -gt 1 ]; then
  echo "Several signing accounts:"; select id in "${ids[@]}"; do [ -n "$id" ] && break; done
  account_id=$id
else
  account_id=${ids[0]}
fi
sub=$(cut -d/ -f3 <<<"$account_id")
api=$(az provider show -n Microsoft.CodeSigning -o json |
  jq -r '.resourceTypes[] | select(.resourceType == "codeSigningAccounts/certificateProfiles") | .apiVersions[]' |
  grep -v preview | head -1)
[ -n "$api" ] || die "could not read the Microsoft.CodeSigning API version"
acct=$(az rest --method get --url "https://management.azure.com$account_id?api-version=$api" -o json)
endpoint=$(jq -r .properties.accountUri <<<"$acct")
account_name=$(jq -r .name <<<"$acct")
tenant=$(az account show --subscription "$sub" -o json | jq -r .tenantId)

# 2. Certificate profile
profiles=$(az rest --method get --url "https://management.azure.com$account_id/certificateProfiles?api-version=$api" -o json |
  jq -r '.value[].name')
[ -n "$profiles" ] || die "the account $account_name has no certificate profile"
if [ -n "${SIGNING_PROFILE:-}" ]; then
  profile=$SIGNING_PROFILE
elif [ "$(wc -l <<<"$profiles")" -gt 1 ]; then
  echo "Several certificate profiles (MLQS uses the one in GitLab's TRUSTED_SIGNING_PROFILE):"
  select profile in $profiles; do [ -n "$profile" ] && break; done
else
  profile=$profiles
fi
profile_id="$account_id/certificateProfiles/$profile"
echo "Signing account: $account_name ($endpoint), profile: $profile"

# 3. App registration with a federated credential for the tag-only GitHub environment
app_id=$(az ad app list --display-name "$APP_NAME" -o json | jq -r '.[0].appId // empty')
if [ -z "$app_id" ]; then
  echo "App registration $APP_NAME does not exist."
  if ask "create app registration $APP_NAME"; then
    app_id=$(az ad app create --display-name "$APP_NAME" --sign-in-audience AzureADMyOrg -o json | jq -r '.appId // empty')
    [ -n "$app_id" ] || die "could not create the app registration (Entra permissions?)"
  fi
else
  echo "App registration $APP_NAME exists ($app_id)."
fi
if [ -n "$app_id" ]; then
  sp=$(az ad sp show --id "$app_id" -o json | jq -r '.id // empty')
  if [ -z "$sp" ] && ask "create its service principal"; then
    sp=$(az ad sp create --id "$app_id" -o json | jq -r '.id // empty')
    [ -n "$sp" ] || die "could not create the service principal"
  fi
  if az ad app federated-credential list --id "$app_id" -o json | jq -e --arg s "$SUBJECT" 'any(.[]; .subject == $s)' >/dev/null; then
    echo "Federated credential for $SUBJECT exists."
  elif ask "add federated credential $SUBJECT"; then
    fc=$(cmd.exe /d /c echo %TEMP% | tr -d '\r')\\booster-ai-fc.json
    jq -n --arg s "$SUBJECT" '{name: "booster-ai-signing-env", issuer: "https://token.actions.githubusercontent.com",
      subject: $s, audiences: ["api://AzureADTokenExchange"], description: "GitHub Actions, tag-only environment"}' \
      > "$(wslpath "$fc")"
    created=$(az ad app federated-credential create --id "$app_id" --parameters "$fc" -o json | jq -r '.subject // empty')
    rm -f "$(wslpath "$fc")"
    [ "$created" = "$SUBJECT" ] || die "could not add the federated credential"
  fi
fi

# 4. Signer role on the certificate profile only
role=$(az role definition list --scope "$profile_id" -o json |
  jq -r '[.[] | select(.roleName | test("Certificate Profile Signer"))][0].name // empty')
[ -n "$role" ] || die "role 'Artifact Signing Certificate Profile Signer' not found"
if [ -n "${sp:-}" ]; then
  if az role assignment list --assignee "$sp" --scope "$profile_id" -o json | jq -e --arg r "$role" 'any(.[]; .roleDefinitionId | endswith($r))' >/dev/null; then
    echo "Signer role already assigned."
  elif ask "assign Certificate Profile Signer on $profile to $APP_NAME"; then
    assigned=$(az role assignment create --assignee-object-id "$sp" --assignee-principal-type ServicePrincipal \
      --role "$role" --scope "$profile_id" -o json | jq -r '.id // empty')
    [ -n "$assigned" ] || die "could not assign the role (needs Owner or User Access Administrator on the account)"
  fi
fi

# 5. GitHub environment secrets/variables and the switch
echo "GitHub ($REPO, environment $ENVIRONMENT):"
if [ -n "${app_id:-}" ] && ask "set AZURE_TENANT_ID, AZURE_CLIENT_ID, SIGNING_ENDPOINT, SIGNING_ACCOUNT, SIGNING_PROFILE"; then
  gh secret set AZURE_TENANT_ID --env "$ENVIRONMENT" -R "$REPO" -b "$tenant"
  gh secret set AZURE_CLIENT_ID --env "$ENVIRONMENT" -R "$REPO" -b "$app_id"
  gh variable set SIGNING_ENDPOINT --env "$ENVIRONMENT" -R "$REPO" -b "$endpoint"
  gh variable set SIGNING_ACCOUNT --env "$ENVIRONMENT" -R "$REPO" -b "$account_name"
  gh variable set SIGNING_PROFILE --env "$ENVIRONMENT" -R "$REPO" -b "$profile"
fi
if ask "turn signing on (repository variable SIGNING_ENABLED=true)"; then
  gh variable set SIGNING_ENABLED -R "$REPO" -b true
fi
echo "Done. The next release tag is signed; role assignments can take a few minutes to apply."
