using AIMS.Server.Domain.Interfaces;

namespace AIMS.Server.Infrastructure.Repositories;


public class MockUserRepository : IUserRepository
{
    public Task<bool> ValidateUserAsync(string username, string password)
    {
        // 数据模拟：硬编码校验
        if (username == "admin" && password == "123456")
        {
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }
}
