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
// Read allowed origins array from configuration if present.
var allowedOriginsFromConfig = configuration.GetSection("AllowedCorsOrigins").Get<string[]>();

builder.Services.AddCors(options =>
{
    // A flexible policy that allows specific origins we expect (production, test, cloudfront, localhost),
    // while keeping some wildcard-like acceptance for vercel preview hosts that match a pattern.
    options.AddPolicy("AllowWeb", policy =>
    {
        policy
            .SetIsOriginAllowed(origin =>
            {
                if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;

                // Explicit production host
                if (uri.Host.Equals("www.yobha.in", StringComparison.OrdinalIgnoreCase)) return true;

                // Known test hosts
                if (uri.Host.Equals("yobha-test-env.vercel.app", StringComparison.OrdinalIgnoreCase)) return true;
                if (uri.Host.Equals("yobha-test-env-aef5.vercel.app", StringComparison.OrdinalIgnoreCase)) return true;
                if (uri.Host.Equals("yobha-frontend-user-test.vercel.app", StringComparison.OrdinalIgnoreCase)) return true;
                if (uri.Host.Equals("yobha-frontend-admin-test.vercel.app", StringComparison.OrdinalIgnoreCase)) return true;

                // Allow Vercel preview branches for this project only, e.g., yobha-test-env-git-<branch>-<hash>.vercel.app
                if (uri.Host.EndsWith(".vercel.app", StringComparison.OrdinalIgnoreCase) &&
                    uri.Host.Contains("yobha-test-env", StringComparison.OrdinalIgnoreCase))
                    return true;

                // CloudFront distribution used for your static site
                if (uri.Host.Equals("d2ze1yjiprz2jo.cloudfront.net", StringComparison.OrdinalIgnoreCase)) return true;

                // Allow localhost for dev
                if (uri.IsLoopback) return true;

                // Also allow anything explicitly provided in appsettings AllowedCorsOrigins
                if (allowedOriginsFromConfig != null)
                {
                    foreach (var allowed in allowedOriginsFromConfig)
                    {
                        if (string.IsNullOrWhiteSpace(allowed)) continue;
                        // Normalize the allowed origin into a Uri and compare host+scheme
                        if (Uri.TryCreate(allowed, UriKind.Absolute, out var allowedUri))
                        {
                            if (uri.Scheme.Equals(allowedUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
                                uri.Host.Equals(allowedUri.Host, StringComparison.OrdinalIgnoreCase))
                                return true;
                        }
                    }
                }

                return false;
            })
            // API endpoints commonly require these
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });

    // A developer-only policy that allows only loopback origins (useful when running APIs locally)
    options.AddPolicy("AllowLocalhostLoopback", policy =>
    {
        policy
            .SetIsOriginAllowed(origin =>
                Uri.TryCreate(origin, UriKind.Absolute, out var u) && u.IsLoopback)
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

// Defensive check/log for JWT config
var cfgLogger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger("Startup");
if (string.IsNullOrWhiteSpace(jwtSettings.Key) ||
    string.IsNullOrWhiteSpace(jwtSettings.Issuer) ||
    string.IsNullOrWhiteSpace(jwtSettings.Audience))
{
    cfgLogger.LogWarning("JWT settings appear incomplete. Ensure Jwt:Key, Jwt:Issuer, Jwt:Audience are set in configuration.");
    // Optionally throw to fail-fast in CI/Prod:
    // throw new InvalidOperationException("JWT is not configured properly.");
}

// ---------------------------
// Repositories & Services
// ---------------------------
// Register repositories & services. Prefer interfaces where available.
builder.Services.AddSingleton<UserRepository>();
builder.Services.AddSingleton<IProductRepository, ProductRepository>();
builder.Services.AddSingleton<OtpRepository>();
builder.Services.AddSingleton<InviteRepository>();
builder.Services.AddSingleton<IWishlistRepository, WishlistRepository>();
builder.Services.AddSingleton<ICartRepository, CartRepository>();
// Use scoped for repositories that may depend on scoped things; adjust as you need
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddSingleton<IStorageService, S3StorageService>();
builder.Services.AddScoped<ICouponRepository, CouponRepository>();
builder.Services.AddScoped<ICouponService, CouponService>();
builder.Services.AddScoped<IReferralRepository, ReferralRepository>();
builder.Services.AddScoped<ReferralService>();
builder.Services.AddScoped<IJobPostingRepository, JobPostingRepository>();
builder.Services.AddScoped<IApplicantRepository, ApplicantRepository>();
builder.Services.AddScoped<IBuybackService, BuybackService>();
builder.Services.AddScoped<ISecretsRepository, MongoSecretsRepository>();
builder.Services.AddScoped<IBuybackService, BuybackService>();
builder.Services.AddScoped<IReturnRepository, ReturnRepository>();
builder.Services.AddScoped<ISmsGatewayService, SmsGatewayService>();
builder.Services.AddScoped<ILoyaltyPointAuditService, LoyaltyPointAuditService>();



// Add memory cache (used by PaymentHelper)
builder.Services.AddMemoryCache();

// Register typed Mongo collections used in helpers/repos
builder.Services.AddScoped(sp => sp.GetRequiredService<IMongoDatabase>().GetCollection<GiftCard>("giftcards"));
builder.Services.AddScoped(sp => sp.GetRequiredService<IMongoDatabase>().GetCollection<Counter>("counters"));
builder.Services.AddScoped(sp => sp.GetRequiredService<IMongoDatabase>().GetCollection<Order>("orders"));
// products collection is accessed directly in repository from IMongoDatabase, still okay, but nothing wrong registering it:
builder.Services.AddScoped(sp => sp.GetRequiredService<IMongoDatabase>().GetCollection<Product>("products"));
builder.Services.AddScoped(sp => sp.GetRequiredService<IMongoDatabase>().GetCollection<ReturnOrder>("returnorders"));
builder.Services.AddScoped(sp => sp.GetRequiredService<IMongoDatabase>().GetCollection<LoyaltyPointAudit>("loyaltyPointAudits"));


// Register GiftCardHelper & PaymentHelper as typed clients/services
// GiftCardHelper will still be resolved as scoped
builder.Services.AddScoped<ShoppingPlatform.Helpers.GiftCardHelper>();

// Register PaymentHelper as a typed HTTP client so its HttpClient parameter is resolved.
// This also registers PaymentHelper in DI so constructor dependencies are injected.
builder.Services.AddHttpClient<ShoppingPlatform.Helpers.PaymentHelper>();


// Avoid duplicate registrations; add HttpClient for Razorpay
builder.Services.AddHttpClient<IRazorpayService, RazorpayService>();

// ---------------------------
// AWS S3
// ---------------------------
builder.Services.Configure<AwsS3Settings>(configuration.GetSection("AwsS3"));
var awsSettings = configuration.GetSection("AwsS3").Get<AwsS3Settings>();
if (awsSettings is not null && !string.IsNullOrEmpty(awsSettings.Region))
{
    var region = Amazon.RegionEndpoint.GetBySystemName(awsSettings.Region);
    if (!string.IsNullOrEmpty(awsSettings.AccessKey) && !string.IsNullOrEmpty(awsSettings.SecretKey))
    {
        var creds = new Amazon.Runtime.BasicAWSCredentials(awsSettings.AccessKey, awsSettings.SecretKey);
        builder.Services.AddSingleton<IAmazonS3>(sp => new AmazonS3Client(creds, region));
    }
}

// Make IS3Service singleton to match IAmazonS3 (stateless)
builder.Services.AddSingleton<IS3Service, S3Service>();

// ---------------------------
// TwoFactor SMS (2factor.in)
// ---------------------------
builder.Services.Configure<TwoFactorSettings>(configuration.GetSection("TwoFactor"));
builder.Services.AddHttpClient<TwoFactorService>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(15);
});
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

// Ensure productId unique index at startup (idempotent)
try
{
    var mongoDb = app.Services.GetRequiredService<IMongoDatabase>();
    var products = mongoDb.GetCollection<ShoppingPlatform.Models.Product>("products");

    var indexKeys = Builders<ShoppingPlatform.Models.Product>.IndexKeys.Ascending(p => p.ProductId);
    var indexOptions = new CreateIndexOptions { Name = "ux_product_productId", Unique = true };
    var model = new CreateIndexModel<ShoppingPlatform.Models.Product>(indexKeys, indexOptions);
    products.Indexes.CreateOne(model);
}
catch (Exception ex)
{
    var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
    startupLogger.LogWarning(ex, "Failed to create unique index on products.productId. Ensure migration run to remove duplicates before enabling unique index.");
}

// Use the appropriate CORS policy depending on environment
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

// IMPORTANT: Ensure CORS middleware runs BEFORE authentication if preflight needs to include auth headers.
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseMiddleware<JwtMiddleware>();
app.UseAuthorization();

app.MapControllers();
app.Run();
