using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Selcomm.Authentication.Common.Extensions;
using Selcomm.Authentication.Common.Handlers;
using FileLoading.Data;
using FileLoading.Interfaces;
using FileLoading.Models;
using FileLoading.Parsers;
using FileLoading.Parsers.Cdr;
using FileLoading.Repositories;
using FileLoading.Services;
using FileLoading.Transfer;
using FileLoading.Validation;
using FileLoading.Workers;
using Microsoft.Extensions.Options;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ============================================
// Load Shared Configuration FIRST, then local overrides
// ============================================
var sharedConfigPath = Environment.GetEnvironmentVariable("SELCOMM_CONFIG_PATH")
    ?? (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? @"C:\Selcomm\configuration\appsettings.shared.json"
        : "/etc/selcomm/appsettings.shared.json");

builder.Configuration
    .AddJsonFile(sharedConfigPath, optional: false, reloadOnChange: true)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

// ============================================
// Configure Serilog
// ============================================
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "FileLoading")
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: "logs/fileloading-.log",
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();

// ============================================
// Configure Services
// ============================================

// Add controllers with JSON options
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
    });

// Configure JWT Authentication with symmetric key
var jwtSettings = builder.Configuration.GetSection("JwtSettings");

// Build list of valid issuers from global and domain-specific settings
var validIssuers = new List<string> { jwtSettings["Issuer"] ?? "AuthenticationApi" };
var domainJwtSettings = builder.Configuration.GetSection("DomainJwtSettings");
foreach (var domain in domainJwtSettings.GetChildren())
{
    var domainIssuer = domain["Issuer"];
    if (!string.IsNullOrEmpty(domainIssuer) && !validIssuers.Contains(domainIssuer))
    {
        validIssuers.Add(domainIssuer);
    }
}

var secretKey = jwtSettings["SecretKey"] ?? throw new InvalidOperationException("JWT SecretKey is not configured");

// Configure Authentication - Multi-scheme (JWT + API Key)
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = "MultiAuth";
    options.DefaultChallengeScheme = "MultiAuth";
})
.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuers = validIssuers,
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey))
    };
})
.AddApiKeyAuthentication()
.AddPolicyScheme("MultiAuth", "JWT or API Key", options =>
{
    options.ForwardDefaultSelector = context =>
    {
        if (context.Request.Headers.ContainsKey("X-API-Key"))
        {
            return ApiKeyAuthenticationOptions.DefaultScheme;
        }
        return JwtBearerDefaults.AuthenticationScheme;
    };
});

// Configure Authorization
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("MultiAuth", policy =>
    {
        policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme, ApiKeyAuthenticationOptions.DefaultScheme);
        policy.RequireAuthenticatedUser();
    });
});

builder.Services.AddHttpContextAccessor();

// Register Database Context and API Key authentication (DbContextBase-aware)
builder.Services.AddScoped<FileLoaderDbContext>();
builder.Services.AddDbContextApiKeyAuthentication<FileLoaderDbContext>();

// Configure FileLoader options (batch sizes, streaming mode, etc.)
builder.Services.Configure<FileLoaderOptionsRoot>(
    builder.Configuration.GetSection("FileLoaderOptions"));

// Register connection factory for services
builder.Services.AddScoped<Func<System.Data.IDbConnection>>(sp =>
{
    var dbContext = sp.GetRequiredService<FileLoaderDbContext>();
    return () => dbContext.GetConnection();
});

// Register Repository
builder.Services.AddScoped<IFileLoaderRepository, FileLoaderRepository>();

// Register File Parsers
builder.Services.AddScoped<IFileParser, GenericCdrParser>();
builder.Services.AddScoped<IFileParser, TelstraGsmCdrParser>();
builder.Services.AddScoped<IFileParser, TelstraCdmaCdrParser>();
builder.Services.AddScoped<IFileParser, OptusCdrParser>();
builder.Services.AddScoped<IFileParser, AaptCdrParser>();
builder.Services.AddScoped<IFileParser, VodafoneCdrParser>();
builder.Services.AddScoped<IFileParser, ChgFileParser>();
builder.Services.AddScoped<IFileParser, SvcFileParser>();
builder.Services.AddScoped<IFileParser, OrdFileParser>();
builder.Services.AddScoped<IFileParser, EblFileParser>();
builder.Services.AddScoped<IFileParser, SssWhlsCdrParser>();
builder.Services.AddScoped<IFileParser, SssWhlsChgParser>();
builder.Services.AddScoped<IFileParser, GenericFileParser>();

// Register Services
builder.Services.AddScoped<IFileLoaderService, FileLoaderService>();
builder.Services.AddScoped<IFileTransferService, FileTransferService>();
builder.Services.AddScoped<IFileManagementService, FileManagementService>();

// Register Transfer utilities
builder.Services.AddScoped<ITransferClientFactory, TransferClientFactory>();
builder.Services.AddScoped<CompressionHelper>();

// Register Validation services
builder.Services.AddScoped<ValidationEngine>();
builder.Services.AddScoped<IValidationConfigProvider, ValidationConfigProvider>();
builder.Services.AddScoped<ErrorAggregator>();

// AI Review services
builder.Services.Configure<AiReviewOptions>(builder.Configuration.GetSection("AiReview"));
builder.Services.AddHttpClient("ClaudeApi", (sp, client) =>
{
    var config = sp.GetRequiredService<IOptions<AiReviewOptions>>().Value;
    client.BaseAddress = new Uri(config.BaseUrl ?? "https://api.anthropic.com");
    client.DefaultRequestHeaders.Add("anthropic-version", config.AnthropicVersion ?? "2023-06-01");
    client.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);
});
builder.Services.AddScoped<IAiReviewService, AiReviewService>();

// Register Background Worker for scheduled transfers
builder.Services.AddHostedService<FileTransferWorker>();

// Configure Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v4", new OpenApiInfo
    {
        Title = "FileLoading API",
        Version = "v4",
        Description = @"## Overview
The FileLoading API provides file loading and processing functionality for the Selcomm platform, including CDR parsing, file transfer management, and validation.

## Key Features

### File Loading
- **CDR Parsing**: Generic and vendor-specific CDR file parsers (Telstra, Optus, AAPT, Vodafone)
- **File Types**: CHG, EBL, SVC, ORD file processing
- **Validation**: Configurable validation engine with error aggregation

### File Transfer Management
- **Source Configuration**: SFTP, FTP, and FileSystem source definitions
- **Scheduled Transfers**: Background worker for automated file downloads
- **Workflow Pipeline**: Transfer → Processing → Processed/Errors/Skipped

## Authentication

All endpoints require authentication:
- **JWT Bearer Token**: `Authorization: Bearer {token}`
- **API Key**: `X-API-Key: {key}`

## Support
Contact: api-support@selcomm.com",
        Contact = new OpenApiContact
        {
            Name = "Selcomm API Support",
            Email = "api-support@selcomm.com"
        }
    });

    // Add JWT Bearer authentication
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    // Add API Key authentication
    options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Description = "API Key authentication. Enter your API key.",
        Name = "X-API-Key",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        },
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "ApiKey"
                }
            },
            Array.Empty<string>()
        }
    });

    // Enable annotations (SwaggerOperation, Tags)
    options.EnableAnnotations();

    // Add tag descriptions for Swagger UI grouping
    options.DocumentFilter<FileLoading.Infrastructure.TagDescriptionsDocumentFilter>();

    // Include XML comments
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// ============================================
// Configure Middleware Pipeline
// ============================================

// Enable Swagger in all environments for now
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v4/swagger.json", "FileLoading API v4");
    options.RoutePrefix = "swagger";
});

app.UseHttpsRedirection();

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
