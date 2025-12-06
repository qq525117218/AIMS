using System.Diagnostics;
using AIMS.Server.Application.DTOs;
using AIMS.Server.Application.DTOs.Health;
using Microsoft.AspNetCore.Mvc;

using StackExchange.Redis;

namespace AIMS.Server.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HealthController : ControllerBase
    {
        private readonly IConnectionMultiplexer _redis;
        
        // ❌ 删除或注释掉下面这行
        // private readonly MySqlDbContext _dbContext; 

        // ✅ 修改构造函数：只保留 IConnectionMultiplexer
        public HealthController(IConnectionMultiplexer redis) 
        {
            _redis = redis;
            // _dbContext = dbContext; // <--- 删除这行
        }
        /// <summary>
        /// 健康检测
        /// </summary>
        /// <remarks>
        /// 检测连接是否正常
        /// </remarks>
        [HttpPost("check")]
        public async Task<ApiResponse<HealthCheckDto>> Check()
        {
            var result = new HealthCheckDto
            {
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Components = new List<ComponentHealth>()
            };

            // 1. 检查 API 自身
            result.Components.Add(new ComponentHealth
            {
                Name = "API Service",
                Status = "Healthy",
                Description = "Service is running",
                Duration = "0ms"
            });

            // 2. 检查 Redis
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            try
            {
                var db = _redis.GetDatabase();
                await db.PingAsync(); // Ping Redis
                stopwatch.Stop();

                result.Components.Add(new ComponentHealth
                {
                    Name = "Redis",
                    Status = "Healthy",
                    Description = "Connection successful",
                    Duration = $"{stopwatch.ElapsedMilliseconds}ms"
                });
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                result.Status = "Unhealthy"; // 更新总体状态
                result.Components.Add(new ComponentHealth
                {
                    Name = "Redis",
                    Status = "Unhealthy",
                    Description = ex.Message,
                    Duration = $"{stopwatch.ElapsedMilliseconds}ms"
                });
            }

            // 3. MySQL 检查 (暂时注释掉，等配置好 EF Core 后再开启)
            /*
            try 
            {
                // await _dbContext.Database.CanConnectAsync();
                // ...
            }
            catch {}
            */

            // 设置总体状态 (如果还没设置为 Unhealthy，默认为 Healthy)
            if (result.Status == "Unknown") result.Status = "Healthy";

            return ApiResponse<HealthCheckDto>.Success(result, "健康检查完成");
        }
    }
}