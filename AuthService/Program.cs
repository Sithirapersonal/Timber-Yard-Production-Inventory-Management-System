// CI/CD pipeline test
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using AuthService.Repositories;
using AuthService.Services;
using Scalar.AspNetCore;


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Register our custom services
builder.Services.AddScoped<UserRepository>();
builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<LoginHistoryRepository>();

// Configure JWT Authentication
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret not configured.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"];
var jwtAudience = builder.Configuration["Jwt:Audience"];

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
options.MapInboundClaims = false;
options.TokenValidationParameters = new TokenValidationParameters
{
    ValidateIssuer = true,
    ValidateAudience = true,
    ValidateLifetime = true,
    ValidateIssuerSigningKey = true,
    ValidIssuer = jwtIssuer,
    ValidAudience = jwtAudience,
    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
    RoleClaimType = "role",
    NameClaimType = System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub
};
});

builder.Services.AddAuthorization();

// CORS: allow the React frontend (running on Vite's dev server) to call this API
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontendDev", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
       app.MapScalarApiReference();

  
}


app.UseHttpsRedirection();

app.UseCors("AllowFrontendDev");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();