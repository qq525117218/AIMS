using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.JsonWebTokens; 

using AIMS.Server.Api.Filters;
using AIMS.Server.Application.Options;
using AIMS.Server.Application.Services;
using AIMS.Server.Domain.Entities;
using AIMS.Server.Domain.Interfaces;
using AIMS.Server.Infrastructure.Auth;
using AIMS.Server.Infrastructure.DataBase;
using AIMS.Server.Infrastructure.Extensions;
using AIMS.Server.Infrastructure.Repositories;
using AIMS.Server.Infrastructure.Services;
using DotNetEnv;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using StackExchange.Redis;

// 1. 加载 .env
Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

// =========================================================================
// 2. 配置绑定
// =========================================================================

// Redis 配置
builder.Services.Configure<RedisOptions>(options => 
{
    builder.Configuration.GetSection(RedisOptions.SectionName).Bind(options);
    var envHost = builder.Configuration["REDIS_HOST"] ?? builder.Configuration["ConnectionStrings:Redis"]?.Split(':')[0];
    if (!string.IsNullOrEmpty(envHost)) options.Host = envHost;
    if (!string.IsNullOrEmpty(builder.Configuration["REDIS_PORT"])) options.Port = builder.Configuration["REDIS_PORT"]!;
    if (!string.IsNullOrEmpty(builder.Configuration["REDIS_PASSWORD"])) options.Password = builder.Configuration["REDIS_PASSWORD"]!;
    if (!string.IsNullOrEmpty(builder.Configuration["REDIS_PREFIX"])) options.Prefix = builder.Configuration["REDIS_PREFIX"]!;
    if (string.IsNullOrEmpty(options.Prefix)) options.Prefix = "AIMS";
});

// JWT 配置
builder.Services.Configure<JwtOptions>(options =>
{
    options.SecretKey = builder.Configuration["JWT_SECRET"] 
                        ?? builder.Configuration["Jwt:SecretKey"]
                        ?? throw new InvalidOperationException("JWT SecretKey is strictly required.");
    options.Issuer = builder.Configuration["Jwt:Issuer"] ?? "AIMS_Server";
    options.Audience = builder.Configuration["Jwt:Audience"] ?? "AIMS_Client";
    if (int.TryParse(builder.Configuration["Jwt:ExpireMinutes"], out int expireMinutes))
        options.ExpireMinutes = expireMinutes;
    else 
        options.ExpireMinutes = 120;
});

var jwtSecret = builder.Configuration["JWT_SECRET"] ?? builder.Configuration["Jwt:SecretKey"];

// =========================================================================
// 3. 服务注册 (DI)
// =========================================================================

var mysqlConnStr = builder.Configuration["MYSQL_CONNECTION_STRING"] 
                   ?? builder.Configuration.GetConnectionString("MySql")
                   ?? $"Server={builder.Configuration["MYSQL_HOST"]};Port={builder.Configuration["MYSQL_PORT"]};Database={builder.Configuration["MYSQL_DATABASE"]};Uid={builder.Configuration["MYSQL_USER"]};Pwd={builder.Configuration["MYSQL_PASSWORD"]};";

builder.Services.AddDbContext<MySqlDbContext>(options => 
    options.UseMySql(mysqlConnStr, ServerVersion.AutoDetect(mysqlConnStr)));

builder.Services.AddSingleton<IConnectionMultiplexer>(sp => 
{
    var opts = sp.GetRequiredService<IOptions<RedisOptions>>().Value;
    var connStr = opts.GetConnectionString(); 
    return ConnectionMultiplexer.Connect(connStr);
});

builder.Services.AddInfrastructureServices();

builder.Services.Configure<PlmOptions>(options =>
{
    options.AppKey = builder.Configuration["PLM_APP_KEY"] ?? throw new InvalidOperationException("Missing PLM_APP_KEY");
    options.AppSecret = builder.Configuration["PLM_APP_SECRET"] ?? throw new InvalidOperationException("Missing PLM_APP_SECRET");
    options.BaseUrl = builder.Configuration["PLM_BASE_URL"] ?? "https://api.thirdparty-plm.com"; 
});

builder.Services.AddScoped<IUserRepository, MockUserRepository>();
builder.Services.AddScoped<IRedisService, RedisService>();
builder.Services.AddScoped<IJwtProvider, JwtProvider>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IPsdService, PsdService>();
builder.Services.AddScoped<IWordService, WordService>();
builder.Services.AddScoped<IWordParser, AsposeWordParser>();
builder.Services.AddScoped<IPlmApiService, PlmApiService>();

builder.Services.AddRouting(options => options.LowercaseUrls = true);

builder.Services.AddControllers(options =>
{
    options.Filters.Add<GlobalExceptionFilter>();
    options.Filters.Add<RequestIdResultFilter>();
})
.AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.JsonSerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "AIMS_Server",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "AIMS_Client",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret!)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
        
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                try 
                {
                    var redis = context.HttpContext.RequestServices.GetRequiredService<IRedisService>();
                    var redisOpts = context.HttpContext.RequestServices.GetRequiredService<IOptions<RedisOptions>>().Value;
                    
                    string rawToken = "";
                    if (context.SecurityToken is JwtSecurityToken jwtToken)
                        rawToken = jwtToken.RawData;
                    else if (context.SecurityToken is JsonWebToken jsonWebToken)
                        rawToken = jsonWebToken.EncodedToken;
                    else
                    {
                        context.Fail("Invalid Token Type");
                        return;
                    }

                    var key = $"{redisOpts.Prefix}:login:{rawToken}";
                    var session = await redis.GetAsync<TokenSession>(key);

                    if (session == null)
                    {
                        context.Fail("Token invalid or expired (logged out).");
                        return;
                    }
                }
                catch (Exception)
                {
                    context.Fail("Internal Auth Error");
                }
            },
            OnAuthenticationFailed = context =>
            {
                Console.WriteLine($"[JWT] Authentication failed: {context.Exception.Message}");
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "AIMS API", Version = "v1" });
    c.SwaggerDoc("plm", new OpenApiInfo { Title = "PLM Module API", Version = "v1", Description = "PLM Module" });
    
    c.DocInclusionPredicate((docName, apiDesc) =>
    {
        var actionGroup = apiDesc.GroupName;
        if (docName == "plm") return actionGroup == "plm";
        if (docName == "v1") return string.IsNullOrEmpty(actionGroup) || actionGroup == "v1";
        return false;
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "请输入 JWT Token",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http, 
        Scheme = "bearer",              
        BearerFormat = "JWT"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            new List<string>()
        }
    });
    
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath)) c.IncludeXmlComments(xmlPath);
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Main API");
        c.SwaggerEndpoint("/swagger/plm/swagger.json", "PLM API");
    });
}

app.UseAuthentication(); 
app.UseAuthorization();

app.MapControllers();

// =========================================================================
// ✅ 核心修复：启动前执行 Aspose 预热
// 这会将“第一次加载慢/崩溃”的问题消灭在启动阶段，而不是在用户请求时
// =========================================================================
await AsposePreheater.PreloadAsync(); 

await app.RunAsync();