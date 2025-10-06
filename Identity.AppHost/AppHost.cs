using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Infisical.Sdk;
using Infisical.Sdk.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

var builder = DistributedApplication.CreateBuilder(args);

// Infisical bootstrap (read creds from user-secrets)
var infisicalUrl =
    builder.Configuration["Infisical:Url"]
    ?? "https://infisical.openislamu.org";
var infisicalProjectId = builder.Configuration["Infisical:ProjectId"];
var infisicalEnv = builder.Configuration["Infisical:Environment"] ?? "dev";
var infisicalClientId = builder.Configuration["Infisical:ClientId"];
var infisicalClientSecret = builder.Configuration["Infisical:ClientSecret"];

var infisicalSettings = new InfisicalSdkSettingsBuilder()
    .WithHostUri(infisicalUrl)
    .Build();
var infisical = new InfisicalClient(infisicalSettings);

// login with universal auth
await infisical.Auth()
    .UniversalAuth()
    .LoginAsync(infisicalClientId!, infisicalClientSecret!);

// helper: load secrets from a path
async Task<IDictionary<string, string>> LoadInfisicalEnvAsync(
    string projectId,
    string envSlug,
    string secretPath
)
{
    var opts = new ListSecretsOptions
    {
        ProjectId = projectId,
        EnvironmentSlug = envSlug,
        SecretPath = secretPath,
        ExpandSecretReferences = true,
        Recursive = true,
        ViewSecretValue = true
    };

    var secrets = await infisical.Secrets().ListAsync(opts);
    if (secrets == null)
    {
        throw new Exception("Infisical returned null secrets list.");
    }

    return secrets.ToDictionary(s => s.SecretKey, s => s.SecretValue);
}

// Load keycloak secrets once
var keycloakSecrets = await LoadInfisicalEnvAsync(
    infisicalProjectId!,
    infisicalEnv,
    "/keycloak"
);

// Load api secrets once
var apiSecrets = await LoadInfisicalEnvAsync(
    infisicalProjectId!,
    infisicalEnv,
    "/api"
);

// Load postgresql secrets once
var postgresqlSecrets = await LoadInfisicalEnvAsync(
    infisicalProjectId!,
    infisicalEnv,
    "/postgresql"
);

var startAfter = DateTime.Now.AddMinutes(1); // Set the start time to 1 minute from now

builder.Services.AddHealthChecks().AddCheck("mycheck", () =>
{
    return DateTime.Now > startAfter ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy();
});

//var prometheus = builder.AddContainer("prometheus", "prom/prometheus", "v3.2.1")
//    .WithBindMount("./Config/prometheus.yaml", "/etc/prometheus", isReadOnly: true)
//    .WithArgs("--web.enable-otlp-receiver", "--config.file=/etc/prometheus/prometheus.yaml")
//    .WithHttpEndpoint(targetPort: 9090, name: "prometheus-http");

//var grafana = builder.AddContainer("grafana", "grafana/grafana")
//    //.WithBindMount("./Config/grafana.yaml", "etc/grafana", isReadOnly: true)
//    .WithBindMount("./Config/grafana-dashboard", "/var/lib/grafana/dashboards", isReadOnly: true)
//    .WithEnvironment("PROMETHEUS_ENPOINT", prometheus.GetEndpoint("prometheus-http"))
//    .WithHttpEndpoint(targetPort: 3000, name: "grafana-http");


// Admin credentials (no longer needed retrive from insifical)
//var kcAdmin = builder.AddParameter("keycloak-admin");
//var kcPass = builder.AddParameter("keycloak-password", secret: true);

// CockroachDB (use TCP for SQL, HTTP for Admin UI)
// FOR PROD remove --insecure and sslmode should be enabled
var crdb = builder.AddContainer("crdb", "cockroachdb/cockroach:v24.1.1")
  .WithArgs("start-single-node", "--insecure")
  .WithVolume("crdb-data", "/cockroach/cockroach-data")
  .WithEndpoint(name: "sql", targetPort: 26257, protocol: System.Net.Sockets.ProtocolType.Tcp) // Remplacement de WithTcpEndpoint par WithEndpoint
  .WithHttpEndpoint(name: "ui", targetPort: 8080); // Admin UI (optional)

//// Phase Two Keycloak container
//var keycloak = builder.AddContainer("keycloak", "quay.io/phasetwo/phasetwo-keycloak:26")
//  // For development purposes do start-dev, in prod do start. also there is the --optimized that interest me but won't do unless research, don't want to break anything.
//  .WithArgs("start-dev")
////.WithEnvironment("KEYCLOAK_ADMIN", keycloakSecrets["KEYCLOAK_ADMIN"])
////.WithEnvironment("KEYCLOAK_ADMIN_PASSWORD", keycloakSecrets["KEYCLOAK_ADMIN_PASSWORD"])
//// Only set if present to avoid KeyNotFound exceptions
//  .WithEnvironment("KEYCLOAK_ADMIN", keycloakSecrets.TryGetValue("KEYCLOAK_ADMIN", out var kcUser) ? kcUser : "")
//  .WithEnvironment("KEYCLOAK_ADMIN_PASSWORD", keycloakSecrets.TryGetValue("KEYCLOAK_ADMIN_PASSWORD", out var kcPass) ? kcPass : "")
//  .WithEnvironment("KC_PROXY_HEADERS", "xforwarded")
//  // For local dev you can use container DNS; for production set your FQDN
//  .WithEnvironment("KC_HOSTNAME", "http://keycloak:8080")
//  .WithEnvironment("KC_DB", "cockroach")
//  .WithEnvironment("KC_DB_URL", "jdbc:cockroachdb://crdb:26257/keycloak?sslmode=disable")
//  .WithEnvironment("KC_DB_URL_PROPERTIES", "useCockroachMetadata=true")
//  .WithEnvironment("KC_TRANSACTION_XA_ENABLED", "false")
//  .WithEnvironment("KC_TRANSACTION_JTA_ENABLED", "false")
//  .WithVolume("kc-data", "/opt/keycloak/data")
//  .WithHttpEndpoint(name: "http", port: 8080, targetPort: 8080)
//  .WaitFor(crdb);

// Phase Two Keycloak container (Keycloak 26 + CockroachDB)
var keycloak = builder
  .AddContainer("keycloak", "quay.io/phasetwo/phasetwo-keycloak:26")
  // start + auto-build so KC_HTTP_RELATIVE_PATH is applied at runtime
  .WithArgs(
    "start",
    "--verbose",
    "--spi-email-template-provider=freemarker-plus-mustache",
    "--spi-email-template-freemarker-plus-mustache-enabled=true",
    "--spi-theme-cache-themes=false"
  )
  // Bootstrap admin (must exist on first start with a fresh DB)
  .WithEnvironment(
    "KC_BOOTSTRAP_ADMIN_USERNAME",
    keycloakSecrets.TryGetValue("KEYCLOAK_ADMIN", out var kcUser) ? kcUser : ""
  )
  .WithEnvironment(
"KC_BOOTSTRAP_ADMIN_PASSWORD",
keycloakSecrets.TryGetValue("KEYCLOAK_ADMIN_PASSWORD", out var kcPass) ? kcPass : ""
  )
  // DB: Cockroach + wrapper driver + required property
  .WithEnvironment("KC_DB", "cockroach")
  .WithEnvironment("KC_DB_URL_HOST", "crdb")
  .WithEnvironment("KC_DB_URL_PORT", "26257")
  .WithEnvironment("KC_DB_URL_DATABASE", "defaultdb")
  .WithEnvironment("KC_DB_SCHEMA", "public")
  .WithEnvironment("KC_DB_USERNAME", "root")
  .WithEnvironment("KC_DB_PASSWORD", "")
  .WithEnvironment("KC_DB_URL_PROPERTIES", "?sslmode=disable&useCockroachMetadata=true")
  .WithEnvironment("KC_TRANSACTION_XA_ENABLED", "false")
  .WithEnvironment("KC_TRANSACTION_JTA_ENABLED", "false")
  // Infinispan JDBC_PING via CRDB (as in Phase Two compose)
  .WithEnvironment("KC_CACHE_CONFIG_FILE", "cache-ispn-jdbc-ping.xml")
  .WithEnvironment("KC_ISPN_DB_VENDOR", "cockroachdb")
  // Network / proxy / mgmt
  .WithEnvironment("KC_HTTP_ENABLED", "true")
  .WithEnvironment("KC_HTTP_RELATIVE_PATH", "/auth")
  .WithEnvironment("KC_PROXY_HEADERS", "xforwarded")
  .WithEnvironment("KC_HOSTNAME_STRICT", "false")
  .WithEnvironment("KC_HEALTH_ENABLED", "true")
  .WithEnvironment("KC_METRICS_ENABLED", "true")
  // Optional: reduce noise or keep Phase Two debug
  .WithEnvironment("KC_LOG_LEVEL", "INFO,io.phasetwo:DEBUG")
  .WithVolume("kc-data", "/opt/keycloak/data")
  .WithHttpEndpoint(name: "http", port: 8080, targetPort: 8080)
  .WithHttpEndpoint(name: "mgmt", port: 9000, targetPort: 9000)
  .WaitFor(crdb);

// Not for now!
//var zipkin = builder.AddContainer("zipkin", "openzipkin/zipkin")
//    .WithEndpoint(9411, 9411, "zipkin-http");

string postgresPassword = postgresqlSecrets.TryGetValue("POSTGRESQL_SERVER_PASSWORD", out var pgServerPass) && pgServerPass != null
    ? pgServerPass
    : "defaultpassword";


var passwordParam = builder.AddParameter("postgres-password", postgresPassword);

//var passwordParam = builder.AddParameter("postgres-password");
//// Injectez la valeur via la configuration
//builder.Configuration["Parameters:postgres-password"] = postgresPassword;

var IdentityServer = builder.AddPostgres("IdentityServer", password: passwordParam)
    .WithDataVolume(isReadOnly: false)
    .WithHealthCheck("mycheck");
var IdentityDB = IdentityServer.AddDatabase("IdentityDB");

//IResourceBuilder<PostgresDatabaseResource> database = builder
//    .AddPostgres("database")
//    .WithImage("postgres:17")
//    .WithBindMount(".containers/db", "/var/lib/postgresql/data")
//    .AddDatabase("clean-architecture");

var realm = keycloakSecrets.TryGetValue("KEYCLOAK_REALM", out var r) ? r : "islamu-dev";
var authority = $"{keycloak.GetEndpoint("http")}/auth/realms/{realm}";

// Pass the HTTP endpoint to the API as an env var
var identityAPI = builder.AddProject<Projects.Identity_API>("identity-api")
  .WithEnvironment("Keycloak__Authority", authority)
  .WithEnvironment("keycloak__Audience", "identity-api")
  .WithEnvironment("keycloak__Realm", keycloakSecrets.TryGetValue("KEYCLOAK_REALM", out var kcRealm) ? kcRealm : "")
  .WithEnvironment("Keycloak__RequireHttpsMetadata", "false")
  .WaitFor(keycloak)
  .WithReference(IdentityDB)
  .WaitFor(IdentityDB);
//.WithEnvirement("ConnectionStrings__IdentityDB", database)
//.WithReference(database)
//.WaitFor(database)
//.WithReference(zipkin);

foreach (var kv in apiSecrets)
{
    identityAPI.WithEnvironment(kv.Key, kv.Value);
}

// Not for now!
//builder.AddRedis("redis");

// Not for now!
//builder.AddRabbitMQ("rabbitmq");

builder.Build().Run();