namespace AIMS.Server.Domain.Interfaces;

public interface IUserRepository
{
    Task<bool> ValidateUserAsync(string username, string password);
}