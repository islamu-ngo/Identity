using Microsoft.Extensions.DependencyInjection;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = DistributedApplication.CreateBuilder(args);

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


// Admin credentials (use secrets in prod)
var kcAdmin = builder.AddParameter("keycloak-admin");
var kcPass = builder.AddParameter("keycloak-password", secret: true);

// CockroachDB (use TCP for SQL, HTTP for Admin UI)
// FOR PROD remove --insecure and sslmode should be enabled
var crdb = builder.AddContainer("crdb", "cockroachdb/cockroach:v24.1.1")
  .WithArgs("start-single-node", "--insecure")
  .WithVolume("crdb-data", "/cockroach/cockroach-data")
  .WithEndpoint(name: "sql", targetPort: 26257, protocol: System.Net.Sockets.ProtocolType.Tcp) // Remplacement de WithTcpEndpoint par WithEndpoint
  .WithHttpEndpoint(name: "ui", targetPort: 8080); // Admin UI (optional)

// Phase Two Keycloak container
var keycloak = builder.AddContainer("keycloak", "quay.io/phasetwo/phasetwo-keycloak:26")
  .WithArgs("start", "--proxy", "edge")
  .WithEnvironment("KEYCLOAK_ADMIN", kcAdmin)
  .WithEnvironment("KEYCLOAK_ADMIN_PASSWORD", kcPass)
  // For local dev you can use container DNS; for production set your FQDN
  .WithEnvironment("KC_HOSTNAME", "http://keycloak:8080")
  .WithEnvironment("KC_DB", "cockroach")
  .WithEnvironment("KC_DB_URL", "jdbc:cockroachdb://crdb:26257/keycloak?sslmode=disable")
  .WithEnvironment("KC_DB_URL_PROPERTIES", "useCockroachMetadata=true")
  .WithEnvironment("KC_TRANSACTION_XA_ENABLED", "false")
  .WithEnvironment("KC_TRANSACTION_JTA_ENABLED", "false")
  .WithVolume("kc-data", "/opt/keycloak/data")
  .WithHttpEndpoint(name: "http", port: 8080, targetPort: 8080)
  .WaitFor(crdb);

var zipkin = builder.AddContainer("zipkin", "openzipkin/zipkin")
    .WithEndpoint(9411, 9411, "zipkin-http");

var IdentityServer = builder.AddPostgres("IdentityServer")
    .WithDataVolume(isReadOnly: false)
    .WithHealthCheck("mycheck");
var IdentityDB = IdentityServer.AddDatabase("IdentityDB");

// Pass the HTTP endpoint to the API as an env var
builder.AddProject<Projects.Identity_API>("identity-api")
  .WithEnvironment("Keycloak__Authority", keycloak.GetEndpoint("http"))
  .WaitFor(keycloak)
  .WithReference(IdentityDB)
  .WaitFor(IdentityDB);
//.WithReference(zipkin);

builder.AddRedis("redis");

builder.AddRabbitMQ("rabbitmq");

builder.Build().Run();