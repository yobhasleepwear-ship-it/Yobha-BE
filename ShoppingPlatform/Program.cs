using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MongoDB.Driver;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Twilio;

using ShoppingPlatform.Configurations;
using ShoppingPlatform.Repositories;
using ShoppingPlatform.Services;
using ShoppingPlatform.Controllers;
using ShoppingPlatform.Middleware; // JwtMiddleware if you add it

var builder = WebApplication.CreateBuilder(args);

// ---------------------------
// Load configuration: JSON + environment variables
// ---------------------------
builder.Configuration
       .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
       .AddEnvironmentVariables();

var configuration = builder.Configuration;

// ---------------------------
// CORS - allowed origins read from config or fallback defaults
// ---------------------------
var allowedOrigins = configuration.GetSection("AllowedCorsOrigins").Get<string[]>()
                     ?? new[]
                     {
                         "https://www.yobha.in",
                         "https://yobha-test-env.vercel.app",
                         "http://localhost:5173",   // frontend dev port
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

if (string.IsNullOrWhiteSpace(mongoSettings.ConnectionString))
{
    Console.WriteLine("Warning: Mongo connection string is empty. Ensure MONGO__CONNECTIONSTRING is set.");
}

builder.Services.AddSingleton<IMongoClient>(_ => new MongoClient(mongoSettings.ConnectionString));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IMongoClient>().GetDatabase(mongoSettings.DatabaseName));

// ---------------------------
// JWT settings + service
// ---------------------------
builder.Services.Configure<JwtSettings>(configuration.GetSection("Jwt"));
var jwtSettings = configuration.GetSection("Jwt").Get<JwtSettings>() ?? new JwtSettings();

if (string.IsNullOrWhiteSpace(jwtSettings.Key))
{
    Console.WriteLine("Warning: JWT key empty. Set JWT__KEY in environment for production.");
}

// Register JwtService (keeps your existing class style). Use Singleton to reuse signing key across requests.
builder.Services.AddSingleton<JwtService>();

// ---------------------------
// Repositories & Services (concrete registrations as in your repo)
// ---------------------------
// Keep concrete classes as you previously used so compile doesn't require interfaces.
builder.Services.AddSingleton<UserRepository>();
builder.Services.AddSingleton<IProductRepository,ProductRepository>();
builder.Services.AddSingleton<OtpRepository>();
builder.Services.AddSingleton<InviteRepository>();
builder.Services.AddSingleton<IWishlistRepository, WishlistRepository>();
builder.Services.AddSingleton<ICartRepository, CartRepository>();
builder.Services.AddSingleton<IOrderRepository, OrderRepository>();

// Storage and SMS sender (concrete)
builder.Services.AddSingleton<IStorageService, S3StorageService>();
builder.Services.AddSingleton<ISmsSender, TwilioSmsSender>();

// ---------------------------
// Twilio (SMS)
// ---------------------------
builder.Services.Configure<TwilioSettings>(configuration.GetSection("Twilio"));
var twilioSettings = configuration.GetSection("Twilio").Get<TwilioSettings>();
if (twilioSettings is not null && !string.IsNullOrEmpty(twilioSettings.AccountSid) && !string.IsNullOrEmpty(twilioSettings.AuthToken))
{
    TwilioClient.Init(twilioSettings.AccountSid, twilioSettings.AuthToken);
}

// ---------------------------
// Google settings
// ---------------------------
builder.Services.Configure<GoogleSettings>(configuration.GetSection("Google"));

// ---------------------------
// AWS S3 settings & storage service
// ---------------------------
builder.Services.Configure<AwsS3Settings>(configuration.GetSection("AwsS3"));
var awsSettings = configuration.GetSection("AwsS3").Get<AwsS3Settings>();

if (awsSettings is not null && !string.IsNullOrEmpty(awsSettings.Region))
{
    var region = RegionEndpoint.GetBySystemName(awsSettings.Region);

    if (!string.IsNullOrEmpty(awsSettings.AccessKey) && !string.IsNullOrEmpty(awsSettings.SecretKey))
    {
        var creds = new BasicAWSCredentials(awsSettings.AccessKey, awsSettings.SecretKey);
        builder.Services.AddSingleton<IAmazonS3>(sp => new AmazonS3Client(creds, region));
    }
    else
    {
        builder.Services.AddSingleton<IAmazonS3>(sp => new AmazonS3Client(region));
    }
}
else
{
    builder.Services.AddSingleton<IAmazonS3>(sp => new AmazonS3Client());
}

// ---------------------------
// HttpClient factory (for services that need it)
// ---------------------------
builder.Services.AddHttpClient();

// ---------------------------
// Add controllers
// ---------------------------
builder.Services.AddControllers();

// ---------------------------
// Authentication: JWT Bearer
// ---------------------------
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    options.SaveToken = true;

    // Use configured key or fallback placeholder (warning already logged above)
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
// Swagger + JWT support
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
// Build app
// ---------------------------
var app = builder.Build();

// Swagger in dev
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Choose CORS policy
if (app.Environment.IsDevelopment())
{
    app.UseCors("AllowLocalhostLoopback");
}
else
{
    app.UseCors("AllowWeb");
}

app.UseHttpsRedirection();

// Authentication must come before Authorization
app.UseAuthentication();

// Optional: Use JwtMiddleware (populates HttpContext.Items["UserId"]) — remove this line if you don't want it.
app.UseMiddleware<JwtMiddleware>();

app.UseAuthorization();

app.MapControllers();

app.Run();
