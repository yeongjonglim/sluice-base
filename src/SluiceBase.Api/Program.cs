using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Data;
using SluiceBase.Api.Endpoints;
using SluiceBase.Api.Extensions;
using SluiceBase.Api.Middleware;
using SluiceBase.Api.Servers;
using SluiceBase.Api.Services;
using SluiceBase.Api.Targets;
using SluiceBase.Core.Branding;
using SluiceBase.Core.Targets;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddNpgsqlDbContext<AppDbContext>("Metadata",
    configureDbContextOptions: opt => { opt.UseSnakeCaseNamingConvention(); });

builder.Services.AddDataProtection()
    .PersistKeysToDbContext<AppDbContext>();

builder.AddSluiceBaseAuth();

builder.Services.Configure<BrandingOptions>(
    builder.Configuration.GetSection(BrandingOptions.SectionName));

builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "X-XSRF-TOKEN";
    o.Cookie.Name = "XSRF-TOKEN";
});

builder.Services.ConfigureHttpJsonOptions(options => { options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()); });

builder.Services.AddOpenApi(x =>
{
    x.MapVogenTypesInOpenApiTransformers();
    x.AddStringEnumSchemaTransformer();
    x.AddNullableArrayItemsSchemaTransformer();
});

builder.Services.AddSingleton<ITargetEngine, PostgresTargetEngine>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IServerConnectionFactory, ServerConnectionFactory>();
builder.Services.AddScoped<ICatalogService, CatalogService>();
builder.Services.AddScoped<ISchemaService, SchemaService>();
builder.Services.AddScoped<IQueryService, QueryService>();

// Register the "vite" HttpClient used by BrandingHtmlMiddleware in dev.
if (builder.Environment.IsDevelopment())
{
    var viteBaseUrl = builder.Configuration["Frontend:BaseUrl"] ?? "http://localhost:5173";
    var viteClientBuilder = builder.Services.AddHttpClient("vite");
    viteClientBuilder.ConfigureHttpClient(c => c.BaseAddress = new Uri(viteBaseUrl));
}

var app = builder.Build();

if (builder.Configuration.GetValue("Migrations:AutoApply", true)
    && builder.Configuration.GetConnectionString("Metadata") is not null)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
});

if (!builder.Environment.IsDevelopment())
{
    app.UseStaticFiles();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

if (builder.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapDefaultEndpoints();
app.MapAllEndpoints();

// Terminal handler: inject branding into index.html for all non-API GET requests.
// In dev: fetches index.html from the Vite dev server and injects branding.
// In prod: reads wwwroot/index.html from disk and injects branding.
app.UseMiddleware<BrandingHtmlMiddleware>();

app.Run();