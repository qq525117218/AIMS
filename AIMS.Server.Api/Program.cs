using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.IdentityModel.Tokens.Jwt;
// ✅ 新增：用于支持 .NET 8+ 的 JsonWebToken
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
// 2. 配置绑定 (统一管理)
// =========================================================================

// Redis 配置：优先使用绑定，确保 logic 一致
builder.Services.Configure<RedisOptions>(options => 
{
    // 1. 先尝试从 appsettings.json 绑定
    builder.Configuration.GetSection(RedisOptions.SectionName).Bind(options);
    
    // 2. 环境变量覆盖
    var envHost = builder.Configuration["REDIS_HOST"] ?? builder.Configuration["ConnectionStrings:Redis"]?.Split(':')[0];
    if (!string.IsNullOrEmpty(envHost)) options.Host = envHost;
    
    if (!string.IsNullOrEmpty(builder.Configuration["REDIS_PORT"])) 
        options.Port = builder.Configuration["REDIS_PORT"]!;
        
    if (!string.IsNullOrEmpty(builder.Configuration["REDIS_PASSWORD"])) 
        options.Password = builder.Configuration["REDIS_PASSWORD"]!;
        
    if (!string.IsNullOrEmpty(builder.Configuration["REDIS_PREFIX"])) 
        options.Prefix = builder.Configuration["REDIS_PREFIX"]!;
    
    // 确保有默认值
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

// Database
var mysqlConnStr = builder.Configuration["MYSQL_CONNECTION_STRING"] 
                   ?? builder.Configuration.GetConnectionString("MySql")
                   ?? $"Server={builder.Configuration["MYSQL_HOST"]};Port={builder.Configuration["MYSQL_PORT"]};Database={builder.Configuration["MYSQL_DATABASE"]};Uid={builder.Configuration["MYSQL_USER"]};Pwd={builder.Configuration["MYSQL_PASSWORD"]};";

builder.Services.AddDbContext<MySqlDbContext>(options => 
    options.UseMySql(mysqlConnStr, ServerVersion.AutoDetect(mysqlConnStr)));

// Redis (关键修改：使用 RedisOptions 构建连接，确保 DB 一致)
builder.Services.AddSingleton<IConnectionMultiplexer>(sp => 
{
    var opts = sp.GetRequiredService<IOptions<RedisOptions>>().Value;
    // 使用 Options 类自带的逻辑生成连接串，确保 Database 参数被包含
    var connStr = opts.GetConnectionString(); 
    return ConnectionMultiplexer.Connect(connStr);
});

// Infrastructure
builder.Services.AddInfrastructureServices();

// PLM Options
builder.Services.Configure<PlmOptions>(options =>
{
    options.AppKey = builder.Configuration["PLM_APP_KEY"] ?? throw new InvalidOperationException("Missing PLM_APP_KEY");
    options.AppSecret = builder.Configuration["PLM_APP_SECRET"] ?? throw new InvalidOperationException("Missing PLM_APP_SECRET");
    options.BaseUrl = builder.Configuration["PLM_BASE_URL"] ?? "https://api.thirdparty-plm.com"; 
});

// Domain & Application Services
builder.Services.AddScoped<IUserRepository, MockUserRepository>();
builder.Services.AddScoped<IRedisService, RedisService>();
builder.Services.AddScoped<IJwtProvider, JwtProvider>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IPsdService, PsdService>();
builder.Services.AddScoped<IWordService, WordService>();
builder.Services.AddScoped<IWordParser, AsposeWordParser>();
builder.Services.AddScoped<IPlmApiService, PlmApiService>();

// Routing
builder.Services.AddRouting(options => options.LowercaseUrls = true);

// Controllers
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

// Authentication
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
            //ClockSkew = TimeSpan.Zero 
            ClockSkew = TimeSpan.FromSeconds(30)
        };
        
        // ✅ 核心修复：带日志的 Redis 状态校验 (兼容 .NET 8)
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                try 
                {
                    // 1. 获取服务
                    var redis = context.HttpContext.RequestServices.GetRequiredService<IRedisService>();
                    var redisOpts = context.HttpContext.RequestServices.GetRequiredService<IOptions<RedisOptions>>().Value;
                    
                    // 2. 获取 Token (兼容处理)
                    string rawToken = "";

                    // 情况 A: 旧版 Token (System.IdentityModel.Tokens.Jwt.JwtSecurityToken)
                    if (context.SecurityToken is JwtSecurityToken jwtToken)
                    {
                        rawToken = jwtToken.RawData;
                    }
                    // 情况 B: 新版 Token (Microsoft.IdentityModel.JsonWebTokens.JsonWebToken) - .NET 8 默认
                    else if (context.SecurityToken is JsonWebToken jsonWebToken)
                    {
                        rawToken = jsonWebToken.EncodedToken;
                    }
                    else
                    {
                       // Console.WriteLine($"[Auth] Unknown Token Type: {context.SecurityToken?.GetType().FullName}");
                        context.Fail("Invalid Token Type");
                        return;
                    }

                    var key = $"{redisOpts.Prefix}:login:{rawToken}";

                    // 3. 查 Redis (Debug 日志)
                  //  Console.WriteLine($"[Auth] Checking Redis Key: {key}");
                    
                    var session = await redis.GetAsync<TokenSession>(key);

                    if (session == null)
                    {
                     //   Console.WriteLine($"[Auth] Session NOT found for key: {key} -> 返回 401");
                        context.Fail("Token invalid or expired (logged out).");
                        return;
                    }

                  //  Console.WriteLine("[Auth] Redis Session Validated ✅");
                }
                catch (Exception ex)
                {
                   // Console.WriteLine($"[Auth] Redis Check Failed: {ex.Message}");
                    // 为了安全，报错也视为验证失败
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

// Swagger (保持不变)
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
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
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

await app.RunAsync();