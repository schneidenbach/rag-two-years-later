---
name: setup-lakeshore
description: Set up and run the "RAG: Two Years Later" Lakeshore Health demos, either replay-only with no Azure or fully live on the person's own Azure subscription (provision, seed, index, optional Foundry hosted agent). Use when someone asks to set up, install, run, deploy or try this repo, or hits a config or Azure error while doing so.
---

# Set up the Lakeshore demos

This sets up the demo apps on the person's machine. There are two paths. Ask which one they want before starting, unless they already said.

- **Replay only** takes about 2 minutes, needs no Azure, and costs nothing. The UI plays back the recorded runs in `recordings/pinned/`.
- **Full live** takes about 30–40 minutes and runs on their own Azure subscription. It creates billable resources: a Foundry account with three model deployments, Azure AI Search Basic, Postgres Flexible Server General Purpose D2ds_v5, Storage, and Container Registry Basic. State the cost up front and get a yes before running `az deployment`.

Rules for every step:
- Run commands from the repo root.
- Never print, echo, or commit the Postgres password or `lakeshore.local.json`. Both are gitignored, so keep it that way.
- Write secrets to files with a script, and `chmod 600` the config.
- Never open the Postgres firewall to all IPs. Allow the person's own IP, plus the `0.0.0.0` "Azure services" rule that the Bicep already adds for the hosted agent.
- After each phase, say in one line what passed. If something fails, show the error and fix the cause; don't paper over it.

## 1. Prerequisites (both paths)

Check for each tool and offer to install whatever is missing (Homebrew on macOS, winget on Windows):
- **.NET 10 SDK:** `dotnet --version` should report 10.x.
- **Node 20 or later:** `node --version`.
- **Full path only:** Azure CLI (`az version`) with Bicep (`az bicep version`), and Python 3.

Then install and build:
```bash
dotnet build Lakeshore.slnx
```
```bash
npm --prefix web ci
```

## 2a. Replay only

The API needs configuration to start, but replay never calls Azure, so placeholder hosts are fine. Write `lakeshore.local.json` (gitignored):
```json
{"Lakeshore": {
  "ProjectEndpoint": "https://replay.invalid/api/projects/replay",
  "OpenAIEndpoint": "https://replay.invalid",
  "SearchEndpoint": "https://replay.invalid",
  "PostgresConnection": "Host=replay.invalid;Database=workorders"
}}
```
Then go to **4. Run and verify**, testing replay only. Tell the person that Live mode won't work with this config, and that they can rerun this skill to do the full setup.

## 2b. Full live: provision Azure

1. **Sign in and pick a subscription.** Run `az login`. Show them `az account show --query "{name:name,id:id}"` and confirm it's the subscription they want.
2. **Check model quota.** The Bicep deploys `gpt-5.5` and `gpt-5.4-mini` at 500K TPM (GlobalStandard), and `text-embedding-3-small` at 350K TPM.
   - Run `az cognitiveservices usage list -l <region> -o table` for their chosen region.
   - If quota is short, lower the `capacity` values in `infra/main.bicep`. 50 is enough for a demo.
   - If a model isn't offered in the region, pick another region and pass it as `location`.
3. **Choose regions.**
   - Ask for a resource group name. The default is `rg-lakeshore`, which `infra/deploy-agent.sh` also assumes.
   - Ask for a region: `location` sets Foundry, Storage and ACR, and `searchLocation` and `pgLocation` can differ.
   - Postgres SKUs vary by subscription type. If the deployment fails with `SkuNotAvailable` or `LocationIsOfferRestricted` for Postgres, retry with a different `pgLocation`. Use a new `pgServerName` each time, because a failed name stays reserved for a while.
4. **Deploy.** Generate a strong Postgres admin password into a shell variable, without printing it. Get the person's IP (`curl -s https://api.ipify.org`) and object id (`az ad signed-in-user show --query id -o tsv`). Then:
   ```bash
   az group create -n rg-lakeshore -l <location>
   ```
   ```bash
   az deployment group create -g rg-lakeshore -f infra/main.bicep -p pgAdminPassword="$PG_PW" clientIp=<ip> userPrincipalId=<object-id> location=<location> --query properties.outputs -o json > infra/outputs.json
   ```
   - The deployment takes about 10–15 minutes.
   - `userPrincipalId` grants the person these roles: OpenAI User and Foundry User on Foundry, Search Service Contributor and Index Data Contributor on Search, and Blob Data Contributor on Storage. The apps authenticate with their `az login`, not with keys.
   - Role assignments can take a few minutes to propagate. A 403 right after the deployment usually goes away on retry.
5. **Write `lakeshore.local.json` from the deployment outputs.** Use a short Python script that reads `infra/outputs.json` and the password from the environment, following `lakeshore.local.example.json`:
   - `ProjectEndpoint`, `OpenAIEndpoint` and `SearchEndpoint` come from the outputs of the same name.
   - `StorageAccount` comes from `storageName`.
   - Build `PostgresConnection` as `Host=<pgHost>;Port=5432;Username=<pgUser>;Password=<pw>;Database=workorders;SslMode=Require`.
   - `chmod 600` the file afterwards.

## 3. Full live: load the data

1. **Seed the work orders.** This creates about 12.5k rows, deterministically, in about 1 minute:
   ```bash
   cd db/Seeder && PG_CONN="$(python3 -c "import json;print(json.load(open('../../lakeshore.local.json'))['Lakeshore']['PostgresConnection'])")" dotnet run -- ../schema.sql; cd ../..
   ```
   If the connection times out, the person's IP probably changed. Add a firewall rule with `az postgres flexible-server firewall-rule create` for the new IP.
2. **Embed the work orders** for the vector-RAG baseline. This takes a few minutes:
   ```bash
   dotnet run --project src/Lakeshore.Console -- setup embed
   ```
3. **Load the documents.** This uploads them to Blob, chunks and embeds them, and creates the index `lakeshore-docs`, the knowledge source `lakeshore-ks`, and the knowledge base `lakeshore-kb`:
   ```bash
   dotnet run --project src/Lakeshore.Console -- setup docs docs-corpus
   ```
4. **Smoke test** from the CLI:
   ```bash
   dotnet run --project src/Lakeshore.Console -- naive-wo "Why do plumbing jobs take so long to close?"
   ```
   ```bash
   dotnet run --project src/Lakeshore.Console -- kb "Is ACME meeting its fill-rate commitment?"
   ```
   The second command should print subqueries and a cited answer that mentions the 95% fill-rate commitment against actuals in the low 70s and 60s.

## 4. Run and verify (both paths)

Start the two servers defined in `.claude/launch.json`: the API on http://localhost:5158 and the web UI on http://localhost:5173. Use `preview_start` if it's available. Otherwise run them in the background:
```bash
dotnet run --project src/Lakeshore.Api --urls http://localhost:5158
```
```bash
npm --prefix web run dev -- --port 5173 --strictPort
```
Open http://localhost:5173, then check:
- **Replay:** press `r`, then click each button. Demo 1 has *Vector RAG · 2024* and *Agent · 2026*. Demo 2 (key `2`) has *Vector RAG · 2024*, *Agentic retrieval* and *Agent · data + docs*. Each one should play back its recorded run.
- **Live, full path only:**
  - Demo 1 *Vector RAG · 2024* takes about 3 seconds.
  - Demo 1 *Agent · 2026* takes about 40 seconds and makes about 14 tool calls. It should end with Finding / Evidence / Ruled out / Recommended next step, pointing at North Campus Tower B and the ACME parts backorders.
  - Demo 2 *Agentic retrieval* takes about 12 seconds.

  Every live run is saved to `recordings/`. The **Recordings** button pins a good take as that mode's replay.

## 5. Optional: Foundry hosted agent (full path)

This deploys the same agent and tools as a Foundry hosted agent with its own Entra identity:
```bash
RG=rg-lakeshore infra/deploy-agent.sh v1
```
The script does the following:
- Builds the image in ACR.
- Stores the Postgres connection string in the project connection `lakeshore-secrets`.
- Creates the agent version and waits for it to go active, which takes a few minutes.
- Grants the agent identity Search Index Data Reader.

Then point the person to https://ai.azure.com › their project › Agents › `lakeshore-facilities` › Playground and ask them to try the Demo 2 question.
- If the playground shows an older version after a redeploy, go back to the Agents list and reopen the agent.
- The first request after the agent has been idle takes longer while the container starts.

## Cleanup

Delete the resource group when finished: `az group delete -n rg-lakeshore`. Confirm with the person first, because this is permanent. Search and Postgres are billed while they exist, even when idle. To pause between sessions, run `az postgres flexible-server stop`.
