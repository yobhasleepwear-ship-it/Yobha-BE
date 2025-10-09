using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MongoDB.Driver;
using Amazon.S3;
using ShoppingPlatform.Configurations;
using ShoppingPlatform.Repositories;
using ShoppingPlatform.Services;
using ShoppingPlatform.Middleware;
using ShoppingPlatform.Models;
using ShoppingPlatform.Sms;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------
// Load configuration
// ---------------------------
builder.Configuration
       .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
       .AddEnvironmentVariables();

var configuration = builder.Configuration;

// ---------------------------
// CORS
// ---------------------------
var allowedOrigins = configuration.GetSection("AllowedCorsOrigins").Get<string[]>()
                     ?? new[]
                     {
                         "https://www.yobha.in",
                         "https://yobha-test-env.vercel.app",
                         "http://localhost:5173",
                         "http://localhost:3000",
                     };

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowWeb", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });

    options.AddPolicy("AllowLocalhostLoopback", policy =>
    {
        policy.SetIsOriginAllowed(origin =>
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                return false;
            return uri.IsLoopback;
        })
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials();
    });
});

// ---------------------------
// MongoDB
// ---------------------------
builder.Services.Configure<MongoDbSettings>(configuration.GetSection("Mongo"));
var mongoSettings = configuration.GetSection("Mongo").Get<MongoDbSettings>() ?? new MongoDbSettings();
builder.Services.AddSingleton<IMongoClient>(_ => new MongoClient(mongoSettings.ConnectionString));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IMongoClient>().GetDatabase(mongoSettings.DatabaseName));

// ---------------------------
// JWT
// ---------------------------
builder.Services.Configure<JwtSettings>(configuration.GetSection("Jwt"));
var jwtSettings = configuration.GetSection("Jwt").Get<JwtSettings>() ?? new JwtSettings();
builder.Services.AddSingleton<JwtService>();

// ---------------------------
// Repositories & Services
// ---------------------------
builder.Services.AddSingleton<UserRepository>();
builder.Services.AddSingleton<IProductRepository, ProductRepository>();
builder.Services.AddSingleton<OtpRepository>();
builder.Services.AddSingleton<InviteRepository>();
builder.Services.AddSingleton<IWishlistRepository, WishlistRepository>();
builder.Services.AddSingleton<ICartRepository, CartRepository>();
builder.Services.AddSingleton<IOrderRepository, OrderRepository>();
builder.Services.AddSingleton<IStorageService, S3StorageService>();

// ---------------------------
// AWS S3
// ---------------------------
builder.Services.Configure<AwsS3Settings>(configuration.GetSection("AwsS3"));
var awsSettings = configuration.GetSection("AwsS3").Get<AwsS3Settings>();
if (awsSettings is not null && !string.IsNullOrEmpty(awsSettings.Region))
{
    var region = Amazon.RegionEndpoint.GetBySystemName(awsSettings.Region);
    var creds = new Amazon.Runtime.BasicAWSCredentials(awsSettings.AccessKey, awsSettings.SecretKey);
    builder.Services.AddSingleton<IAmazonS3>(sp => new AmazonS3Client(creds, region));
}
builder.Services.AddScoped<IS3Service, S3Service>();

// ---------------------------
// TwoFactor SMS (2factor.in)
// ---------------------------
builder.Services.Configure<TwoFactorSettings>(configuration.GetSection("TwoFactor"));
builder.Services.AddHttpClient<TwoFactorService>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(15);
});

// Add the TwoFactorSmsSender that accepts IConfiguration and ILogger
builder.Services.AddSingleton<ISmsSender, TwoFactorSmsSender>();

// Optional: Log presence of TwoFactor key at startup (non-secret)
var twoFactorApiKey = configuration["TwoFactor:ApiKey"]
                       ?? Environment.GetEnvironmentVariable("TWOFACTOR__APIKEY")
                       ?? Environment.GetEnvironmentVariable("TWOFACTOR_APIKEY");

var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger("Startup");
if (string.IsNullOrWhiteSpace(twoFactorApiKey))
{
    logger.LogWarning("TwoFactor API key not found in TwoFactor:ApiKey or env vars (TWOFACTOR__APIKEY / TWOFACTOR_APIKEY).");
}
else
{
    logger.LogInformation("TwoFactor API key presence confirmed (length={Length}).", twoFactorApiKey.Length);
}

// ---------------------------
// Google settings
// ---------------------------
builder.Services.Configure<GoogleSettings>(configuration.GetSection("Google"));

// ---------------------------
// Controllers & Auth
// ---------------------------
builder.Services.AddControllers();

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    options.SaveToken = true;
    var key = string.IsNullOrEmpty(jwtSettings.Key) ? "ReplaceThisInEnvWithStrongKey" : jwtSettings.Key;

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidateAudience = true,
        ValidAudience = jwtSettings.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
        ValidateLifetime = true,
    };
});

// ---------------------------
// Swagger
// ---------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the JWT token only (the UI will add 'Bearer ')."
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Id = "Bearer",
                    Type = ReferenceType.SecurityScheme
                }
            },
            new List<string>()
        }
    });
});

// ---------------------------
// Build & Run
// ---------------------------
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseCors("AllowLocalhostLoopback");
}
else
{
    app.UseCors("AllowWeb");
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseMiddleware<JwtMiddleware>();
app.UseAuthorization();
app.MapControllers();
app.Run();
