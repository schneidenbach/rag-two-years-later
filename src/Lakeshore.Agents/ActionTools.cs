using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace Lakeshore.Agents;

/// <summary>The "do something about it" tool. Drafts only — nothing is ever sent.</summary>
public sealed class ActionTools
{
    public const string DraftToolName = "draft_escalation";

    public IEnumerable<AIFunction> AsAIFunctions() => [AIFunctionFactory.Create(DraftEscalation, DraftToolName)];

    [Description("""
        Draft (never send) a concrete next-step message for a human to review: a vendor escalation, an SOP change request,
        or a note to facilities leadership. Include the specific evidence: work order numbers, metrics, and document citations.
        """)]
    public string DraftEscalation(
        [Description("Recipient role or organization, e.g. 'ACME Mechanical Supply - Account Manager'.")] string to,
        [Description("Subject line.")] string subject,
        [Description("Message body in markdown. Concise, specific, professional.")] string body,
        [Description("Work order numbers cited as evidence.")] string[]? evidenceWorkOrders = null,
        [Description("Document citations, e.g. 'ACME Supply Agreement §4.2'.")] string[]? documentCitations = null,
        [Description("Specific asks / remedies requested.")] string[]? asks = null)
    {
        return new JsonObject
        {
            ["kind"] = "escalation_draft",
            ["status"] = "Draft saved for review. Not sent.",
            ["to"] = to,
            ["subject"] = subject,
            ["body"] = body,
            ["evidenceWorkOrders"] = JsonSerializer.SerializeToNode(evidenceWorkOrders ?? []),
            ["documentCitations"] = JsonSerializer.SerializeToNode(documentCitations ?? []),
            ["asks"] = JsonSerializer.SerializeToNode(asks ?? []),
        }.ToJsonString();
    }
}
