using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder.Extensions;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.

// Read directly
var authority = builder.Configuration["Keycloak:Authority"];
var realm = builder.Configuration["Keycloak:Realm"];
var audience = builder.Configuration["Keycloak:Audience"];

//var jwtKey = builder.Configuration["JwtKey"];

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddAuthentication()
    .AddKeycloakJwtBearer("keycloak", realm: realm, options =>
    {
        // will require when in prod... needs to fix this hardcoded value later...
        //options.RequireHttpsMetadata = false;

        // Sets options.RequireHttpsMetadata to true if the config value is "true", otherwise false
        options.RequireHttpsMetadata = string.Equals(
            builder.Configuration["Keycloak:RequireHttpsMetadata"],
            "true",
            StringComparison.OrdinalIgnoreCase
        );

        options.Authority = authority;
        options.Audience = audience;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            // it only checks if envirement variable isnullorempty... I need audience to contain identity-api !!
            //ValidateAudience = !string.IsNullOrEmpty(builder.Configuration["Keycloak:Audience"]),
            ValidAudiences = new[] { "identity-api" },
            ValidateLifetime = true,
            NameClaimType = "preferred_username",
            RoleClaimType = "roles" // see mapper note below
        };
    });


//builder.Services.AddAuthorization(options =>
//{
//    options.AddPolicy("ApiUser", policy => policy.RequireRole("api.user"));
//    options.AddPolicy("ApiAdmin", policy => policy.RequireRole("api.admin"));
//});

builder.Services.AddAuthorization();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapDefaultEndpoints();

//app.UseExceptionHandler();

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/whoami", (HttpContext ctx) =>
{
    var user = ctx.User;
    return Results.Ok(new
    {
        isAuthenticated = user?.Identity?.IsAuthenticated ?? false,
        name = user?.Identity?.Name,
        roles = user?.Claims.Where(c => c.Type == "roles").Select(c => c.Value)
    });
});

app.MapGet("users/me", (ClaimsPrincipal claimsPrincipal) =>
{
    return claimsPrincipal.Claims.ToDictionary(c => c.Type, c => c.Value);
}).RequireAuthorization();

app.MapGet("/admin-only", () => Results.Ok("secret")).RequireAuthorization("ApiAdmin");

app.Run();