using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Shieldsmith MCP server: exposes parsed Power Platform solutions over stdio so
// Claude Code (or any MCP client) can query them live.
//
// Register with:  claude mcp add shieldsmith -- <path>\Shieldsmith.Mcp.exe

var builder = Host.CreateApplicationBuilder(args);

// stdio transport: stdout is the protocol channel, so logging goes to stderr.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
