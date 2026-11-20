#pragma warning disable MAAI001 // AgentSessionStore is preview; we only alias the default registration.
using Azure.Core;
using Azure.Identity;
using Lakeshore.Agents;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry.Hosting;

var config = LakeshoreConfig.Build();
var settings = LakeshoreSettings.From(config);
TokenCredential credential = new DefaultAzureCredential();
var lake = new LakeshoreAgents(settings, credential);
var agent = lake.Create(AgentKind.Full);

// Must be the IHostApplicationBuilder overload: the IServiceCollection one throws at startup when hosted.
var builder = WebApplication.CreateBuilder(args);
builder.AddFoundryResponses(agent);

// Foundry sends agent.name = the hosted agent's name and looks it up as a keyed service first.
// Alias that key to the default registrations so the log stream doesn't warn on every request.
const string FoundryAgentName = "lakeshore-facilities";
builder.Services.AddKeyedSingleton<AIAgent>(FoundryAgentName, (sp, _) => sp.GetRequiredService<AIAgent>());
builder.Services.AddKeyedSingleton<AgentSessionStore>(FoundryAgentName, (sp, _) => sp.GetRequiredService<AgentSessionStore>());

var app = builder.Build();
// Fail fast at startup rather than on the first playground request if the aliases don't resolve.
_ = app.Services.GetRequiredKeyedService<AIAgent>(FoundryAgentName);
_ = app.Services.GetRequiredKeyedService<AgentSessionStore>(FoundryAgentName);
app.MapFoundryResponses();
app.Run();
