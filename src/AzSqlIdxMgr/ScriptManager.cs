
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Sql;
using Microsoft.Data.SqlClient;
using Tingle.Extensions.Primitives;

namespace AzSqlIdxMgr;

public class ScriptManager
{
    private static readonly string CreateProcedureCommandText = string.Join(Environment.NewLine,
        "if object_id('AzureSQLMaintenance') is null",
        "\texec('create procedure AzureSQLMaintenance as /*dummy procedure body*/ select 1;')");
    private static readonly string ExecutionProcedureCommandText = "exec AzureSQLMaintenance 'all'";

    private readonly ILogger logger;
    private readonly string? alterProcedureCommandText;

    public ScriptManager(ILogger<ScriptManager> logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

        using var stream = typeof(Program).Assembly.GetManifestResourceStream($"{typeof(ScriptManager).Namespace}.AzureSQLMaintenance.txt")!;
        using var sr = new StreamReader(stream);
        alterProcedureCommandText = sr.ReadToEnd();
    }

    internal async Task<int> ExecuteAsync(string[] subscriptions,
                                          string[] serverNames,
                                          string[] databaseNames,
                                          bool interactive,
                                          int maxExecutionTries,
                                          TimeSpan executionTimeout,
                                          bool dryRun,
                                          CancellationToken cancellationToken = default)
    {
        var credential = new DefaultAzureCredential(includeInteractiveCredentials: interactive);
        var client = new ArmClient(credential);

        logger.LogInformation("Fetching subscriptions ...");
        var subs = client.GetSubscriptions().GetAllAsync(cancellationToken);
        await foreach (var sub in subs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // if we have a list of subscriptions to check, skip the ones not in the list
            if (subscriptions.Length > 0
                && !subscriptions.Contains(sub.Data.SubscriptionId, StringComparer.OrdinalIgnoreCase)
                && !subscriptions.Contains(sub.Data.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                logger.LogDebug("Skipping subscription '{SubscriptionName}' ...", sub.Data.DisplayName); // no subscription ID for security reasons
                continue;
            }

            logger.LogInformation("Working in {SubscriptionName}", sub.Data.DisplayName); // no subscription ID for security reasons

            var servers = sub.GetSqlServersAsync(cancellationToken: cancellationToken);
            await foreach (var server in servers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // if server names are specified, skip if this one is not among them
                if (serverNames.Length > 0 && !serverNames.Contains(server.Data.Name, StringComparer.OrdinalIgnoreCase))
                {
                    logger.LogDebug("Skipping database '{DatabaseName}' ...", server.Data.Name); // no FQDN for security reasons
                    continue;
                }

                logger.LogInformation("Beginning maintenance for databases in '{ServerName}'.", server.Data.Name);

                var databases = server.GetSqlDatabases().GetAllAsync(cancellationToken: cancellationToken);
                await foreach (var database in databases)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // do not modify master database
                    if ("master".Equals(database.Data.Name)) continue;

                    // skip non-primary databases
                    var replications = database.GetSqlServerDatabaseReplicationLinks().GetAllAsync(cancellationToken);
                    bool primary = true;
                    await foreach (var repl in replications)
                    {
                        primary &= repl.Data.Role is Azure.ResourceManager.Sql.Models.SqlServerDatabaseReplicationRole.Primary;
                    }
                    if (!primary) continue;

                    // if database names are specified, skip if this one is not among them
                    if (databaseNames.Length > 0 && !databaseNames.Contains(database.Data.Name, StringComparer.OrdinalIgnoreCase))
                        continue;

                    try
                    {
                        await DoMaintenanceAsync(server, database, maxExecutionTries, executionTimeout, dryRun, cancellationToken);
                    }
                    catch (SqlException se) when (se.Number == 40615)
                    {
                        logger.LogWarning("Connection to '{ServerName}' failed because the current IP is not allowed.",
                                          server.Data.Name);
                    }
                    catch (SqlException se) when (se.Number == 18456)
                    {
                        logger.LogWarning("Login to '{ServerName}/{DatabaseName}' failed because the user is not allowed.",
                                          server.Data.Name,
                                          database.Data.Name);
                    }
                    catch (SqlException se) when (se.Number == 3906)
                    {
                        logger.LogWarning("Database '{ServerName}/{DatabaseName}' cannot be updated because the user does not have permissions to or the database is read-only.",
                                          server.Data.Name,
                                          database.Data.Name);
                    }
                }

                logger.LogInformation("Maintenance for databases in '{ServerName}' completed", server.Data.Name);
            }
        }

        logger.LogInformation("Finished");
        return 0;
    }

    private async Task DoMaintenanceAsync(SqlServerResource server,
                                          SqlDatabaseResource database,
                                          int maxExecutionTries,
                                          TimeSpan executionTimeout,
                                          bool dryRun,
                                          CancellationToken cancellationToken)
    {
        for (var i = 0; i < maxExecutionTries; i++)
        {
            try
            {
                await DoMaintenanceAsync(server, database, executionTimeout, dryRun, cancellationToken);
                break;
            }
            catch (SqlException se) when (se.Number == 2)
            {
                logger.LogWarning("One or more operations in '{ServerName}/{DatabaseName}' timed out.",
                                  server.Data.Name,
                                  database.Data.Name);
            }
        }
    }

    private async Task DoMaintenanceAsync(SqlServerResource server,
                                          SqlDatabaseResource database,
                                          TimeSpan executionTimeout,
                                          bool dryRun,
                                          CancellationToken cancellationToken)
    {
        logger.LogInformation("Beginning index rebuild '{ServerName}/{DatabaseName}' ...", server.Data.Name, database.Data.Name);

        // prepare connection string
        var csb = new ConnectionStringBuilder("Persist Security Info=False;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;")
        {
            ["Server"] = $"{server.Data.FullyQualifiedDomainName},1433",
            ["Authentication"] = "Active Directory Default",
            ["Database"] = database.Data.Name
        };
        var connectionString = csb.ToString();

        // create connection
        logger.LogDebug("Creating and opening database connection for '{ServerName}/{DatabaseName}'{Suffix}",
                        server.Data.Name,
                        database.Data.Name,
                        dryRun ? " (dry run)" : "");
        using var connection = new SqlConnection(connectionString);
        void logSql(object sender, SqlInfoMessageEventArgs e) => logger.LogDebug("{Message}", e.Message);
        connection.InfoMessage += logSql;
        if (!dryRun) await connection.OpenAsync(cancellationToken);

        // create the stored procedure if it is not there
        logger.LogDebug("Executing create if not exists script for '{ServerName}/{DatabaseName}'{Suffix}",
                        server.Data.Name,
                        database.Data.Name,
                        dryRun ? " (dry run)" : "");
        using var command = new SqlCommand(CreateProcedureCommandText, connection);
        if (!dryRun) await command.ExecuteNonQueryAsync(cancellationToken);
        logger.LogDebug("Completed create if not exists script for '{ServerName}/{DatabaseName}'{Suffix}",
                        server.Data.Name,
                        database.Data.Name,
                        dryRun ? " (dry run)" : "");

        // alter the stored procedure
        logger.LogDebug("Executing alter script for '{ServerName}/{DatabaseName}'{Suffix}",
                        server.Data.Name,
                        database.Data.Name,
                        dryRun ? " (dry run)" : "");
        command.CommandText = alterProcedureCommandText;
        if (!dryRun) await command.ExecuteNonQueryAsync(cancellationToken);
        logger.LogDebug("Completed alter script for '{ServerName}/{DatabaseName}'{Suffix}",
                        server.Data.Name,
                        database.Data.Name,
                        dryRun ? " (dry run)" : "");

        // execute stored procedure
        logger.LogDebug("Executing stored procedure for '{ServerName}/{DatabaseName}'{Suffix}",
                        server.Data.Name,
                        database.Data.Name,
                        dryRun ? " (dry run)" : "");
        command.CommandText = ExecutionProcedureCommandText;
        command.CommandTimeout = Convert.ToInt32(executionTimeout.TotalSeconds);
        if (!dryRun) await command.ExecuteNonQueryAsync(cancellationToken);
        logger.LogDebug("Completed stored procedure running for '{ServerName}/{DatabaseName}'{Suffix}",
                        server.Data.Name,
                        database.Data.Name,
                        dryRun ? " (dry run)" : "");

        logger.LogInformation("Maintenance of database indexes completed for '{ServerName}/{DatabaseName}'", server.Data.Name, database.Data.Name);
    }
}
