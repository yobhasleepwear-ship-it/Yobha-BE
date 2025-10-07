using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MongoDB.Driver;
using Amazon;
using Amazon.S3;
using Twilio;

using ShoppingPlatform.Configurations;
using ShoppingPlatform.Repositories;
using ShoppingPlatform.Services;
using ShoppingPlatform.Controllers;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// ---------------------------
// CORS - read allowed origins from config or use defaults for local dev
// ---------------------------
var allowedOrigins = configuration.GetSection("AllowedCorsOrigins").Get<string[]>()
                     ?? new[]
                     {
                         "http://localhost:5173",   // common frontend dev port
                         "http://localhost:3000",
                         "https://localhost:5001",  // swagger / other
                         "https://localhost:7272"   // API itself
                     };


// CORS - allow any localhost/loopback origin (dev only)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowLocalhostLoopback", policy =>
    {
        policy
            // Allow any localhost/127.0.0.1/[::1] origin (any port)
            .SetIsOriginAllowed(origin =>
            {
                // make sure origin is a valid uri
                if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                    return false;

                // Accept loopback addresses (localhost, 127.0.0.1, ::1)
                return uri.IsLoopback;
            })
            .AllowAnyMethod()
            .AllowAnyHeader();
        // .AllowCredentials(); // be careful: only enable if you need cookies & you set explicit origins
    });
});

// ---------------------------
// MongoDB
// ---------------------------
builder.Services.Configure<MongoDbSettings>(configuration.GetSection("Mongo"));
var mongoSettings = configuration.GetSection("Mongo").Get<MongoDbSettings>()!;
builder.Services.AddSingleton<IMongoClient>(_ => new MongoClient(mongoSettings.ConnectionString));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IMongoClient>().GetDatabase(mongoSettings.DatabaseName));

// ---------------------------
// JWT
// ---------------------------
builder.Services.Configure<JwtSettings>(configuration.GetSection("Jwt"));
var jwtSettings = configuration.GetSection("Jwt").Get<JwtSettings>()!;

// ---------------------------
// Repositories & Services
// ---------------------------
builder.Services.AddSingleton<UserRepository>();
builder.Services.AddSingleton<JwtService>();
builder.Services.AddSingleton<ProductRepository>();
builder.Services.AddSingleton<OtpRepository>();
builder.Services.AddSingleton<InviteRepository>();

// ---------------------------
// Twilio (SMS) - configuration + init
// ---------------------------
builder.Services.Configure<TwilioSettings>(configuration.GetSection("Twilio"));
var twilioSettings = configuration.GetSection("Twilio").Get<TwilioSettings>();
if (twilioSettings is not null && !string.IsNullOrEmpty(twilioSettings.AccountSid) && !string.IsNullOrEmpty(twilioSettings.AuthToken))
{
    TwilioClient.Init(twilioSettings.AccountSid, twilioSettings.AuthToken);
}
builder.Services.AddSingleton<ISmsSender, TwilioSmsSender>();

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
    builder.Services.AddSingleton<IAmazonS3>(sp => new AmazonS3Client(region));
}
else
{
    builder.Services.AddSingleton<IAmazonS3>(sp => new AmazonS3Client());
}
builder.Services.AddSingleton<IStorageService, S3StorageService>();

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
    options.RequireHttpsMetadata = false; // set true in production
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidateAudience = true,
        ValidAudience = jwtSettings.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key)),
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

var app = builder.Build();

// Swagger in dev
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// IMPORTANT: UseCors should be before Authentication/Authorization and before MapControllers
app.UseCors("AllowLocalhostLoopback");

// Authentication must come before Authorization
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
