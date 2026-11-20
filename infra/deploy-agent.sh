#!/usr/bin/env bash
# Deploys the Lakeshore facilities agent as a Foundry hosted agent.
#   1. builds the image in ACR (remote build, linux/amd64)
#   2. stores the Postgres connection string in a CustomKeys project connection
#   3. creates a new hosted agent version via the Foundry REST API and waits for it to go active
#   4. grants the agent's Entra identity read access to the search index
# Usage: RG=<resource-group> infra/deploy-agent.sh [image-tag]      (run from the repo root, after `az login`)
set -euo pipefail

TAG="${1:-v$(date +%Y%m%d%H%M%S)}"
RG="${RG:-rg-lakeshore}"   # the resource group you deployed infra/main.bicep into
SUB=$(az account show --query id -o tsv)
one() { az resource list -g "$RG" --resource-type "$1" --query "[0].name" -o tsv; }
ACCT=$(one Microsoft.CognitiveServices/accounts)
ACR=$(one Microsoft.ContainerRegistry/registries)
SEARCH=$(one Microsoft.Search/searchServices)
[[ -n "$ACCT" && -n "$ACR" && -n "$SEARCH" ]] || { echo "Couldn't find Foundry, ACR and Search in resource group $RG. Set RG=<your group>." >&2; exit 1; }
PROJECT="${PROJECT:-ignite-rag-demo}"
AGENT=lakeshore-facilities   # must match FoundryAgentName in src/Lakeshore.HostedAgent/Program.cs
CONN=lakeshore-secrets
BASE="https://$ACCT.services.ai.azure.com/api/projects/$PROJECT"
IMAGE="$ACR.azurecr.io/lakeshore-agent:$TAG"

PG_CONN=$(python3 -c "import json;print(json.load(open('lakeshore.local.json'))['Lakeshore']['PostgresConnection'])")
case "$PG_CONN" in *localhost*) echo "lakeshore.local.json still points Postgres at localhost; the hosted agent can't reach that." >&2; exit 1;; esac

if [[ "${SKIP_BUILD:-}" != "1" ]]; then
  echo "== build $IMAGE"
  az acr build -r "$ACR" -t "lakeshore-agent:$TAG" --platform linux/amd64 -f src/Lakeshore.HostedAgent/Dockerfile . -o none
fi

echo "== project connection $CONN"
az rest --method put -o none \
  --url "https://management.azure.com/subscriptions/$SUB/resourceGroups/$RG/providers/Microsoft.CognitiveServices/accounts/$ACCT/projects/$PROJECT/connections/$CONN?api-version=2025-06-01" \
  --body "$(python3 -c "import json,sys;print(json.dumps({'properties':{'category':'CustomKeys','authType':'CustomKeys','target':'_','isSharedToAll':False,'credentials':{'keys':{'pg':sys.argv[1]}}}}))" "$PG_CONN")"

echo "== create agent version"
TOKEN=$(az account get-access-token --resource https://ai.azure.com --query accessToken -o tsv)
BODY=$(python3 - "$IMAGE" "$ACCT" "$SEARCH" "$CONN" <<'EOF'
import json, sys
image, acct, search, conn = sys.argv[1:]
print(json.dumps({
  "description": "Lakeshore Health facilities analyst: work-order data (Postgres) + facilities documents (Azure AI Search knowledge base) + drafted escalations.",
  "definition": {
    "kind": "hosted",
    "container_configuration": {"image": image},
    "cpu": "1", "memory": "2Gi",
    "protocol_versions": [{"protocol": "responses", "version": "2.0.0"}],
    "environment_variables": {
      "Lakeshore__OpenAIEndpoint": f"https://{acct}.openai.azure.com",
      "Lakeshore__SearchEndpoint": f"https://{search}.search.windows.net",
      "Lakeshore__ChatDeployment": "gpt-5.5",
      "Lakeshore__MiniDeployment": "gpt-5.4-mini",
      "Lakeshore__ReasoningEffort": "low",
      "Lakeshore__PostgresConnection": "${{connections.%s.credentials.pg}}" % conn,
    },
  },
}))
EOF
)
EXISTS=$(curl -sS -o /dev/null -w '%{http_code}' "$BASE/agents/$AGENT?api-version=v1" -H "Authorization: Bearer $TOKEN")
if [[ "$EXISTS" == "200" ]]; then URL="$BASE/agents/$AGENT/versions?api-version=v1"
else URL="$BASE/agents?api-version=v1"; BODY=$(echo "$BODY" | python3 -c "import json,sys;b=json.load(sys.stdin);b['name']='$AGENT';print(json.dumps(b))"); fi
RESP=$(curl -sS -X POST "$URL" -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" -d "$BODY")
echo "$RESP" | head -c 1500; echo
VERSION=$(echo "$RESP" | python3 -c "
import json,sys
r=json.load(sys.stdin)
print(r.get('version') or r.get('versions',{}).get('latest',{}).get('version') or '')")
[[ -n "$VERSION" ]] || { echo "create failed"; exit 1; }
echo; echo "version $VERSION"

for i in $(seq 1 60); do
  V=$(curl -sS "$BASE/agents/$AGENT/versions/$VERSION?api-version=v1" -H "Authorization: Bearer $TOKEN")
  STATUS=$(echo "$V" | python3 -c "import json,sys;print(json.load(sys.stdin).get('status'))")
  echo "status: $STATUS"
  [[ "$STATUS" == "active" ]] && break
  [[ "$STATUS" == "failed" ]] && { echo "$V"; exit 1; }
  sleep 10
done
echo "$V" > infra/agent-version.json

echo "== grant agent identity search read"
AGENT_PID=$(python3 -c "
import json
v=json.load(open('infra/agent-version.json'))
def find(o):
    if isinstance(o,dict):
        for k,x in o.items():
            if k in ('principal_id','principalId') and isinstance(x,str): return x
            r=find(x)
            if r: return r
    if isinstance(o,list):
        for x in o:
            r=find(x)
            if r: return r
print(find(v) or '')")
if [[ -z "$AGENT_PID" ]]; then echo "No principal id on the version object; see infra/agent-version.json"; exit 1; fi
SEARCH_ID=$(az search service show -g "$RG" -n "$SEARCH" --query id -o tsv)
az role assignment create --assignee-object-id "$AGENT_PID" --assignee-principal-type ServicePrincipal --role "Search Index Data Reader" --scope "$SEARCH_ID" -o none || true
echo "Agent identity $AGENT_PID can read $SEARCH."
echo "Playground: https://ai.azure.com  ->  project $PROJECT  ->  Agents  ->  $AGENT"
