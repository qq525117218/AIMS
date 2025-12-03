using Microsoft.EntityFrameworkCore;

namespace AIMS.Server.Infrastructure.DataBase;

public class MySqlDbContext : DbContext
{
    public MySqlDbContext(DbContextOptions<MySqlDbContext> options): base(options)
    {
    }
    
    
}