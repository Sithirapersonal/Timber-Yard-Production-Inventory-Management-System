using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using SawmillService.Repositories;
using SawmillService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Configure Swagger with JWT Authorize support
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "SawmillService API",
        Version = "v1",
        Description = "Microservice for managing saw jobs and sawn timber stock allocation."
    });

    // Enables the Authorize button in Swagger UI
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your valid JWT token below."
    });
});

// Configure JWT Authentication — same Jwt:Secret/Issuer/Audience as LogIntakeService
var jwtSecret = builder.Configuration["Jwt:Secret"] ?? "a-long-random-string-at-least-32-characters-please";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "TimberYardAuth";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "TimberYardApp";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false;
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

// Repository
builder.Services.AddScoped<ISawmillRepository, SawmillRepository>();

// LogIntakeClient — typed HttpClient with base address from config
var logIntakeApiUrl = builder.Configuration["Services:LogIntakeApiUrl"]
    ?? "http://localhost:5201/api/LogIntake/";

// Ensure trailing slash for relative URL resolution
if (!logIntakeApiUrl.EndsWith('/')) logIntakeApiUrl += '/';

builder.Services.AddHttpClient<LogIntakeClient>(client =>
{
    client.BaseAddress = new Uri(logIntakeApiUrl);
    client.Timeout = TimeSpan.FromSeconds(15);
});

// CORS — same AllowFrontend policy as LogIntakeService (any origin/header/method)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "SawmillService v1");
    });
}

app.UseCors("AllowFrontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
