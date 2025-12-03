namespace AIMS.Server.Domain.Interfaces;

public interface IRedisService
{
    Task SetAsync<T>(string key, T value, TimeSpan expiry);
    Task<T?> GetAsync<T>(string key);
    Task RemoveAsync(string key); // 新增删除接口用于登出
}