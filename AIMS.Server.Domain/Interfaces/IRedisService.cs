using System.Threading.Tasks;

namespace AIMS.Server.Domain.Interfaces;

public interface IRedisService
{
    Task SetAsync<T>(string key, T value, TimeSpan expiry);
    
    /// <summary>
    /// 原子操作：仅当键不存在时设置值 (Set if Not Exists)
    /// </summary>
    /// <returns>是否设置成功（true=抢锁成功，false=锁已存在）</returns>
    Task<bool> SetNxAsync<T>(string key, T value, TimeSpan expiry);
    
    Task<T?> GetAsync<T>(string key);
    Task RemoveAsync(string key);
}