using Microsoft.Extensions.DependencyInjection;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.Identity_API>("identity-api");

builder.Build().Run();