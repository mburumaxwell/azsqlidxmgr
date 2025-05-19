using AzSqlIdxMgr;

var builder = Host.CreateApplicationBuilder();

builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Logging:LogLevel:Default"] = "Information",
    ["Logging:LogLevel:Microsoft"] = "Warning",
    ["Logging:LogLevel:Microsoft.Hosting.Lifetime"] = "Warning",
    ["Logging:Debug:LogLevel:Default"] = "None",

    ["Logging:LogLevel:AzSqlIdxMgr"] = builder.Environment.IsDevelopment() ? "Trace" : "Information",

    ["Logging:Console:FormatterName"] = "cli",
    ["Logging:Console:FormatterOptions:SingleLine"] = "True",
    ["Logging:Console:FormatterOptions:IncludeCategory"] = "False",
    ["Logging:Console:FormatterOptions:IncludeEventId"] = "False",
    ["Logging:Console:FormatterOptions:TimestampFormat"] = "yyyy-MM-dd HH:mm:ss ",
});

// configure logging
builder.Logging.AddCliConsole();

// register services
builder.Services.AddTransient<ScriptManager>();

// build and start the host
using var host = builder.Build();
await host.StartAsync();

// prepare the root command
var subscriptionsOption = new Option<string[]>(name: "--subscription") { Description = "Name or ID of subscriptions allowed. If none are provided, all subscriptions are checked.", };
var serverNamesOption = new Option<string[]>(name: "--server-name") { Description = "The name of the server to work on. When none are provided, all servers except system ones are worked on.", };
var databaseNamesOption = new Option<string[]>(name: "--database-name") { Description = "The name of the databases to work on. When none are provided, all databases except system ones are worked on.", };
var interactiveOption = new Option<bool>(name: "--interactive") { Description = "Allow interactive authentication mode (opens a browser for authentication).", };
var maxExecutionTriesOption = new Option<int>(name: "--max-tries") { Description = "Maximum number of tries executing the SQL script.", DefaultValueFactory = _ => 6, };
var executionTimeoutOption = new Option<TimeSpan>(name: "--execution-timeout") { Description = "Maximum number of tries executing the SQL script.", DefaultValueFactory = _ => TimeSpan.FromMinutes(60), };
var dryRunOption = new Option<bool>(name: "--dry-run") { Description = "Test the logic without actually running the script.", };
var root = new RootCommand("Azure SQL Index Manager") {
    subscriptionsOption,
    serverNamesOption,
    databaseNamesOption,
    interactiveOption,
    dryRunOption,
};
root.SetAction((parseResult, cancellationToken) =>
{
    var subscriptions = parseResult.GetValue(subscriptionsOption) ?? [];
    var serverNames = parseResult.GetValue(serverNamesOption) ?? [];
    var databaseNames = parseResult.GetValue(databaseNamesOption) ?? [];
    var interactive = parseResult.GetValue(interactiveOption);
    var maxExecutionTries = parseResult.GetValue(maxExecutionTriesOption);
    var executionTimeout = parseResult.GetValue(executionTimeoutOption);
    var dryRun = parseResult.GetValue(dryRunOption);

    using var scope = host.Services.CreateScope();
    var provider = scope.ServiceProvider;
    var manager = provider.GetRequiredService<ScriptManager>();
    return manager.ExecuteAsync(subscriptions,
                                serverNames,
                                databaseNames,
                                interactive,
                                maxExecutionTries,
                                executionTimeout,
                                dryRun,
                                cancellationToken);
});

var configuration = new CommandLineConfiguration(root);

// execute the command
try
{
    return await configuration.InvokeAsync(args);
}
finally
{
    // stop the host, this will stop and dispose the services which flushes OpenTelemetry data
    await host.StopAsync();
}
