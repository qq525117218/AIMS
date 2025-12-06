
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using AIMS.Server.Api.Filters;
using AIMS.Server.Application.Options;
using AIMS.Server.Application.Services;
using AIMS.Server.Domain.Interfaces;
using AIMS.Server.Infrastructure.Auth;
using AIMS.Server.Infrastructure.DataBase;
using AIMS.Server.Infrastructure.Extensions;
using AIMS.Server.Infrastructure.Repositories;
using AIMS.Server.Infrastructure.Services;
using DotNetEnv;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using StackExchange.Redis;

// 1. 加载 .env 到环境变量，这会使得它们可以通过 Configuration 访问
Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

// =========================================================================
// 2. 配置绑定 (Options Pattern)
// =========================================================================
// 这种方式支持 appsettings.json 也支持环境变量覆盖 (如 AIMS__Redis__Host)
builder.Services.Configure<RedisOptions>(options => 
{
    options.Host = builder.Configuration["REDIS_HOST"] ?? builder.Configuration["ConnectionStrings:Redis"]?.Split(':')[0] ?? "127.0.0.1";
    // 简单解析，实际项目建议完善解析逻辑
    options.Port = builder.Configuration["REDIS_PORT"] ?? "6379"; 
    options.Password = builder.Configuration["REDIS_PASSWORD"] ?? "";
    options.Prefix = builder.Configuration["REDIS_PREFIX"] ?? "AIMS";
});

builder.Services.Configure<JwtOptions>(options =>
{
    // 优先读取环境变量，其次 appsettings.json
    options.SecretKey = builder.Configuration["JWT_SECRET"] 
                        ?? builder.Configuration["Jwt:SecretKey"]
                        ?? throw new InvalidOperationException("JWT SecretKey is missing. Please check appsettings.json or .env");
    
    options.Issuer = builder.Configuration["Jwt:Issuer"] ?? "AIMS_Server";
    options.Audience = builder.Configuration["Jwt:Audience"] ?? "AIMS_Client";
    
    if (int.TryParse(builder.Configuration["Jwt:ExpireMinutes"], out int expireMinutes))
    {
        options.ExpireMinutes = expireMinutes;
    }
    else 
    {
        options.ExpireMinutes = 120; // 默认值
    }
});

// 获取 JWT Secret 用于鉴权配置
var jwtSecret = builder.Configuration["JWT_SECRET"] ?? builder.Configuration["Jwt:SecretKey"];
if (string.IsNullOrEmpty(jwtSecret))
{
    throw new InvalidOperationException("JWT SecretKey is strictly required for startup.");
}

// 准备 Redis 连接字符串 (用于单例注入)
var redisHost = builder.Configuration["REDIS_HOST"] ?? "127.0.0.1";
var redisPort = builder.Configuration["REDIS_PORT"] ?? "6379";
var redisPwd = builder.Configuration["REDIS_PASSWORD"] ?? "";
var redisConnStr = $"{redisHost}:{redisPort},password={redisPwd},abortConnect=false";

// =========================================================================
// 3. 服务注册 (DI)
// =========================================================================

// Database (EF Core)
// 优先从环境变量读取完整连接串，如果没有则尝试拼接或读取 appsettings
var mysqlConnStr = builder.Configuration["MYSQL_CONNECTION_STRING"] 
                   ?? builder.Configuration.GetConnectionString("MySql")
                   ?? $"Server={builder.Configuration["MYSQL_HOST"]};Port={builder.Configuration["MYSQL_PORT"]};Database={builder.Configuration["MYSQL_DATABASE"]};Uid={builder.Configuration["MYSQL_USER"]};Pwd={builder.Configuration["MYSQL_PASSWORD"]};";

builder.Services.AddDbContext<MySqlDbContext>(options => 
    options.UseMySql(mysqlConnStr, ServerVersion.AutoDetect(mysqlConnStr)));

// Redis
builder.Services.AddSingleton<IConnectionMultiplexer>(sp => 
    ConnectionMultiplexer.Connect(redisConnStr));

// Infrastructure Services (通过扩展方法注册基础设施层服务，包含 Aspose License 初始化)
builder.Services.AddInfrastructureServices();

builder.Services.Configure<PlmOptions>(options =>
{
    options.AppKey = builder.Configuration["PLM_APP_KEY"] 
                     ?? throw new InvalidOperationException("Missing PLM_APP_KEY in .env");
                     
    options.AppSecret = builder.Configuration["PLM_APP_SECRET"] 
                        ?? throw new InvalidOperationException("Missing PLM_APP_SECRET in .env");
                        
    // 假设你在 .env 也配了 PLM_BASE_URL，或者在这里硬编码默认值
    options.BaseUrl = builder.Configuration["PLM_BASE_URL"] ?? "https://api.thirdparty-plm.com"; 
});

// Domain & Application Services
builder.Services.AddScoped<IUserRepository, MockUserRepository>(); // ⚠️ TODO: 替换为真实 UserRepository
builder.Services.AddScoped<IRedisService, RedisService>();
builder.Services.AddScoped<IJwtProvider, JwtProvider>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IPsdService, PsdService>();
builder.Services.AddScoped<IWordService, WordService>();
builder.Services.AddScoped<IWordParser, AsposeWordParser>();
builder.Services.AddScoped<IPlmApiService, PlmApiService>();

// ✅ 新增：配置路由使用小写 URL
// 这会将 [controller] 替换后的名称自动转换为小写 (例如 Auth -> auth)
// 并且 Swagger 文档中的接口路径也会变成小写
builder.Services.AddRouting(options => 
{
    options.LowercaseUrls = true;
});
// Controller & Filters & JSON Options
builder.Services.AddControllers(options =>
{
    // 1. 注册异常拦截器 (处理报错情况)
    options.Filters.Add<GlobalExceptionFilter>();
    
    // 2. ✅ 新增：注册 RequestId 自动填充拦截器 (处理成功情况)
    options.Filters.Add<RequestIdResultFilter>();
})
.AddJsonOptions(options =>
{
    // ✅ 关键配置：将所有属性名称转换为下划线小写 (Snake Case)
    // 例如：ProductName -> product_name, RequestId -> request_id
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
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew = TimeSpan.Zero 
        };
        
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                Console.WriteLine($"[JWT] Authentication failed: {context.Exception.Message}");
                return Task.CompletedTask;
            }
        };

        // ⚠️ 调试建议：如果遇到 401 错误，请先注释掉下面的 Events 块。
        // 因为如果 Redis 中没有对应的 Token (例如服务重启、Redis清空、或Login没写入)，
        // OnTokenValidated 会直接让请求失败。
        /*
        options.Events = new JwtBearerEvents
        {
            // 校验 Redis 中的 Session 是否存在 (实现单点登录/强制下线)
            OnTokenValidated = async context =>
            {
                var redis = context.HttpContext.RequestServices.GetRequiredService<IRedisService>();
                // 获取配置的前缀
                var redisOptsMonitor = context.HttpContext.RequestServices.GetRequiredService<IOptionsMonitor<RedisOptions>>();
                var prefix = redisOptsMonitor.CurrentValue.Prefix;
                
                var token = context.SecurityToken as JwtSecurityToken; // 获取原始 Token 对象
                if (token == null)
                {
                    context.Fail("Invalid Token structure");
                    return;
                }

                var rawToken = token.RawData;
                // Key 格式必须与 AuthService.LoginAsync 中存储的格式一致
                var key = $"{prefix}:login:{rawToken}";
                
                var session = await redis.GetAsync<TokenSession>(key);
                if (session == null)
                {
                    context.Fail("Token invalid or expired (logged out).");
                }
            }
        };
        */
    });

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "AIMS API", Version = "v1" });
    
    // ✅ 2. 新增：定义 PLM 分组
    c.SwaggerDoc("plm", new OpenApiInfo { Title = "PLM Module API", Version = "v1", Description = "对接第三方 PLM 系统的专用接口" });
    
    
    // =========================================================================
    // ✅ 新增：配置文档包含规则 (关键修复)
    // =========================================================================
    c.DocInclusionPredicate((docName, apiDesc) =>
    {
        // 获取当前接口的分组名 (未加特性的接口默认为 null)
        var actionGroup = apiDesc.GroupName;

        // 规则 1: 如果当前生成的文档是 'plm'
        // -> 只有当接口明确标记为 "plm" 时才包含
        if (docName == "plm")
        {
            return actionGroup == "plm";
        }

        // 规则 2: 如果当前生成的文档是 'v1' (主文档)
        // -> 包含明确标记为 "v1" 的接口，或者 没标记分组的接口 (归为主文档)
        if (docName == "v1")
        {
            return string.IsNullOrEmpty(actionGroup) || actionGroup == "v1";
        }

        return false;
    });
    // ✅ [修复] JWT 认证配置：使用 HTTP Bearer 模式
    // 这样在 Swagger UI 中只需粘贴 Token，无需手动输入 "Bearer " 前缀
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "请输入 JWT Token (无需输入 Bearer 前缀，直接粘贴 Token)",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http, // 改为 Http
        Scheme = "bearer",              // 必须是 bearer (不区分大小写)
        BearerFormat = "JWT"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
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
            new List<string>()
        }
    });
    
    // ✅ [已启用] XML 文档注释
    // 确保你的 .csproj 文件中包含了 <GenerateDocumentationFile>true</GenerateDocumentationFile>
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath)) 
    {
        c.IncludeXmlComments(xmlPath);
    }
});

var app = builder.Build();



// =========================================================================
// 4. 管道配置
// =========================================================================

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        // 下拉框选项 1：主 API
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Main API");
        
        // 下拉框选项 2：PLM 模块
        c.SwaggerEndpoint("/swagger/plm/swagger.json", "PLM API");
    });
}

// ⚠️ 注意：认证 (Authentication) 必须在 授权 (Authorization) 之前
app.UseAuthentication(); 
app.UseAuthorization();

app.MapControllers();

await app.RunAsync();