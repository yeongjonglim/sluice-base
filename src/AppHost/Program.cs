using AppHost.Extensions;

var builder = DistributedApplication.CreateBuilder(args);

var main = builder.AddPostgres("main-pg").WithDataVolume();

var metadataDb = main.AddDatabase("metadata-db");
var keycloakDb = main.AddDatabase("keycloak-db");

const string appDbName = "appdb";

var blueDbInstance = builder.AddPostgres("target-blue-pg")
    // Set the name of the default database to auto-create on container startup.
    .WithEnvironment("POSTGRES_DB", appDbName)
    // Mount the SQL scripts directory into the container so that the init scripts run.
    .WithBindMount("seed/blue", "/docker-entrypoint-initdb.d")
    .WithDataVolume();

// Add the default database to the application model so that it can be referenced by other resources.
var blueDb = blueDbInstance.AddDatabase("blue-appdb", appDbName);

var greenDbInstance = builder.AddPostgres("target-green-pg")
    // Set the name of the default database to auto-create on container startup.
    .WithEnvironment("POSTGRES_DB", appDbName)
    // Mount the SQL scripts directory into the container so that the init scripts run.
    .WithBindMount("seed/green", "/docker-entrypoint-initdb.d")
    .WithDataVolume();

// Add the default database to the application model so that it can be referenced by other resources.
var greenDb = greenDbInstance.AddDatabase("green-appdb", appDbName);

var keycloak = builder.AddKeycloak("keycloak")
    .WithRealmImport("seed/keycloak")
    .WithOtlpExporter()
    .WaitFor(keycloakDb)
    .WithPostgres(keycloakDb);

var api = builder.AddProject<Projects.SluiceBase_Api>("api")
    .WithReference(metadataDb, "Metadata").WaitFor(metadataDb)
    .WaitFor(keycloak)
    .WithEnvironment("Oidc__Authority",
        ReferenceExpression.Create($"{keycloak.GetEndpoint("https")}/realms/sluicebase"))
    .WithEnvironment("Oidc__ClientId", "sluicebase-app")
    .WithEnvironment("Oidc__ClientSecret", "dev-secret");

var web = builder.AddViteApp("web", "../frontend")
    .WithNpm(install: true)
    .WithReference(api)
    .WithEnvironment("VITE_API_URL",
        ReferenceExpression.Create($"{api.GetEndpoint("https")}"))
    .WithEndpoint("http", e => { e.Port = 5173; });

api.WithEnvironment("Frontend__BaseUrl",
    ReferenceExpression.Create($"{web.GetEndpoint("http")}"));

metadataDb.WithCommand(
    name: "seed-servers",
    displayName: "Seed Server Registry",
    executeCommand: context => DevServerSeed.SeedAsync(context, api, metadataDb, blueDb, greenDb),
    commandOptions: new CommandOptions
    {
        UpdateState = ctx => ctx.ResourceSnapshot.HealthStatus is Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy
            ? ResourceCommandState.Enabled
            : ResourceCommandState.Disabled,
        IconName = "DatabaseArrowDown",
        IconVariant = IconVariant.Filled,
        Description = "Inserts Blue and Green dev servers. Idempotent.",
        ConfirmationMessage = "Seed dev server records into the registry?",
    });

builder.Build().Run();