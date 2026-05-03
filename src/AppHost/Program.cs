var builder = DistributedApplication.CreateBuilder(args);

var metadata = builder.AddPostgres("metadata-pg")
    .WithDataVolume()
    .AddDatabase("metadata");

builder.AddPostgres("target-blue-pg")
    .WithBindMount("seed/blue", "/docker-entrypoint-initdb.d")
    .WithDataVolume()
    .AddDatabase("blue-appdb", "appdb");

builder.AddPostgres("target-green-pg")
    .WithBindMount("seed/green", "/docker-entrypoint-initdb.d")
    .WithDataVolume()
    .AddDatabase("green-appdb", "appdb");

var keycloak = builder.AddKeycloak("keycloak")
    .WithRealmImport("seed/keycloak");

var api = builder.AddProject<Projects.SluiceBase_Api>("api")
    .WithReference(metadata, "Metadata").WaitFor(metadata)
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

builder.Build().Run();