#!/usr/bin/env bash
set -euo pipefail

KCADM=/opt/keycloak/bin/kcadm.sh
SERVER=${KEYCLOAK_SERVER:-http://keycloak:8080}
REALM=conexao-solidaria

authenticated=false
for _ in $(seq 1 12); do
  if timeout 20 "$KCADM" config credentials \
    --server "$SERVER" \
    --realm master \
    --user "$KEYCLOAK_ADMIN" \
    --password "$KEYCLOAK_ADMIN_PASSWORD" >/dev/null 2>&1; then
    authenticated=true
    break
  fi
  sleep 3
done

if [[ "$authenticated" != true ]]; then
  echo "Nao foi possivel autenticar no Keycloak em $SERVER." >&2
  exit 1
fi

"$KCADM" update "realms/$REALM" \
  -s 'displayName=Conexao Solidaria' \
  -s 'displayNameHtml=Conex&atilde;o Solid&aacute;ria' \
  -s 'loginTheme=conexao-solidaria' \
  -s 'internationalizationEnabled=true' \
  -s 'supportedLocales=["pt-BR"]' \
  -s 'defaultLocale=pt-BR' >/dev/null

"$KCADM" update users/profile -r "$REALM" -f /seed/user-profile.json >/dev/null

if [[ -n "${KEYCLOAK_WEB_CLIENT_SECRET:-}" ]]; then
  web_client_id=$("$KCADM" get clients -r "$REALM" -q clientId=conexao-web --fields id --format csv --noquotes | tail -n 1)
  "$KCADM" update "clients/$web_client_id" -r "$REALM" \
    -s publicClient=false \
    -s clientAuthenticatorType=client-secret \
    -s "secret=$KEYCLOAK_WEB_CLIENT_SECRET" >/dev/null
fi

create_user() {
  local username="$1"
  local first_name="$2"
  local last_name="$3"
  local tenant_id="$4"
  local role="$5"
  local password="$6"

  local user_id
  user_id=$("$KCADM" get users -r "$REALM" -q "username=$username" --fields id --format csv --noquotes 2>/dev/null | tail -n 1 || true)

  if [[ -z "$user_id" ]]; then
    "$KCADM" create users -r "$REALM" \
      -s "username=$username" \
      -s "email=$username" \
      -s "firstName=$first_name" \
      -s "lastName=$last_name" \
      -s "enabled=true" \
      -s "emailVerified=true" >/dev/null

    user_id=$("$KCADM" get users -r "$REALM" -q "username=$username" --fields id --format csv --noquotes | tail -n 1)
  fi

  "$KCADM" update "users/$user_id" -r "$REALM" \
    -s "attributes={\"tenant_id\":[\"$tenant_id\"]}" >/dev/null
  "$KCADM" set-password -r "$REALM" --username "$username" --new-password "$password" >/dev/null
  "$KCADM" add-roles -r "$REALM" --uusername "$username" --rolename "$role" >/dev/null
}

create_user "gestor.esperanca@conexaosolidaria.local" "Gestor" "Esperanca" "esperanca-solidaria" "GestorONG" "$DEMO_MANAGER_PASSWORD"
create_user "doador.esperanca@conexaosolidaria.local" "Doador" "Esperanca" "esperanca-solidaria" "Doador" "$DEMO_DONOR_PASSWORD"
create_user "gestor.mare@conexaosolidaria.local" "Gestor" "Mare Limpa" "mare-limpa" "GestorONG" "$DEMO_MANAGER_PASSWORD"
create_user "doador.mare@conexaosolidaria.local" "Doador" "Mare Limpa" "mare-limpa" "Doador" "$DEMO_DONOR_PASSWORD"
create_user "gestor.futuro@conexaosolidaria.local" "Gestor" "Futuro em Rede" "futuro-em-rede" "GestorONG" "$DEMO_MANAGER_PASSWORD"
create_user "doador.futuro@conexaosolidaria.local" "Doador" "Futuro em Rede" "futuro-em-rede" "Doador" "$DEMO_DONOR_PASSWORD"

echo "Usuarios locais do Keycloak configurados."
