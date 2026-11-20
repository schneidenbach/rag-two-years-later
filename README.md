# RAG: Two Years Later

Demos from the Microsoft Ignite talk *RAG: Two Years Later*. A fictional hospital system, Lakeshore Health, has a facilities problem: plumbing work orders started taking twice as long to close. Each demo asks the same question two ways. First, 2024-style vector RAG: retrieve the top-k chunks and answer. Then a 2026 agent that plans, queries, and checks its work.

- **Demo 1: agentic search over structured data.** A C# [Microsoft Agent Framework](https://github.com/microsoft/agent-framework) agent investigates about 12.5k work orders in Postgres using typed analytics tools, not text-to-SQL. Vector RAG finds tickets that *sound like* the question. The agent finds the cause: a vendor switch and backordered parts in one building.
- **Demo 2: agentic retrieval over documents.** An Azure AI Search knowledge base breaks a question into subqueries over contracts, SOPs, and meeting minutes. The agent then combines work-order evidence with contract clauses and drafts a cited escalation. The same agent also runs as a Microsoft Foundry hosted agent.

Everything in this repo is synthetic: the work orders, the people, the vendors, and the documents.

## Layout

| Path | What it is |
|---|---|
| `src/Lakeshore.Agents` | Agents, tools, Azure AI Search knowledge-base client, vector-RAG baselines |
| `src/Lakeshore.Api` | Minimal API that streams runs to the UI over SSE and records them for replay |
| `src/Lakeshore.Console` | CLI: chat with the agent, run one-off queries, load data |
| `src/Lakeshore.HostedAgent` | The same agent packaged as a Foundry hosted agent |
| `web` | React stage UI |
| `db` | Postgres schema and a deterministic work-order seeder |
| `docs-corpus` | Synthetic facilities documents for the knowledge base |
| `infra` | Bicep for the Azure resources and the hosted-agent deploy script |
| `recordings/pinned` | Recorded runs you can replay without Azure |

## Running it

The repo ships a Claude Code skill that handles setup. Open the repo in [Claude Code](https://claude.com/claude-code) and run:

```
/setup-lakeshore
```

It asks whether you want **replay only**, which needs no Azure (just .NET 10 and Node), or the **full live setup**. The full setup provisions Azure resources, seeds the data, and optionally deploys the hosted agent. The steps live in `.claude/skills/setup-lakeshore/SKILL.md` if you'd rather follow them by hand.

The full setup creates billable resources, including a General Purpose Postgres server and a Basic Azure AI Search service. Delete the resource group when you're done.

Stage UI keys: `1`/`2` switch demos, `r` toggles Live/Replay, `z` zooms for a projector, `Esc` stops a run.
