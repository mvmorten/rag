using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rag.Core;
using Rag.Core.Answering;
using Rag.Core.Search;

// stdio MCP server: stdout carries the protocol, so all logging goes to stderr.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Warning);

var settings = builder.Configuration.GetSection("Rag").Get<RagSettings>()
    ?? throw new InvalidOperationException("Missing 'Rag' configuration section.");
var azure = new AzureClients(settings);

builder.Services.AddSingleton<IRetriever>(new Retriever(azure.SearchClient, azure.Embeddings));
builder.Services.AddSingleton(azure.Chat);
builder.Services.AddSingleton<AnswerService>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
