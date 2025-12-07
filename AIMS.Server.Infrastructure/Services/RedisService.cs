using System.Text.Json;
using AIMS.Server.Domain.Interfaces;
using StackExchange.Redis;

namespace AIMS.Server.Infrastructure.Services;

public class RedisService : IRedisService
{
    private readonly IConnectionMultiplexer _redis;

    public RedisService(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan expiry)
    {
        var db = _redis.GetDatabase();
        await db.StringSetAsync(key, JsonSerializer.Serialize(value), expiry);
    }

    // ✅ 新增：原子锁实现
    public async Task<bool> SetNxAsync<T>(string key, T value, TimeSpan expiry)
    {
        var db = _redis.GetDatabase();
        // When.NotExists 保证了操作的原子性
        return await db.StringSetAsync(key, JsonSerializer.Serialize(value), expiry, When.NotExists);
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        var db = _redis.GetDatabase();
        var value = await db.StringGetAsync(key);
        if (value.IsNullOrEmpty) return default;
        return JsonSerializer.Deserialize<T>(value!);
    }

    public async Task RemoveAsync(string key)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync(key);
    }
}