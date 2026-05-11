using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Data;
using SluiceBase.Api.Endpoints;
using SluiceBase.Api.Extensions;
using SluiceBase.Api.Servers;
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
builder.Services.AddHttpClient("branding");

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
});

builder.Services.AddSingleton<ITargetEngine, PostgresTargetEngine>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IServerConnectionFactory, ServerConnectionFactory>();

var app = builder.Build();

if (app.Environment.IsDevelopment()
    && builder.Configuration.GetValue("Migrations:AutoApply", false))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapOpenApi();
app.MapDefaultEndpoints();
app.MapAllEndpoints();

app.Run();

public partial class Program;