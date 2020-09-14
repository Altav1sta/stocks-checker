using Microsoft.EntityFrameworkCore;
using WebApp.Models;

namespace WebApp
{
    public class ApplicationDbContext : DbContext
    {
        public DbSet<Chat> Chats { get; set; }

        public ApplicationDbContext(DbContextOptions options) : base(options)
        {
        }
    }
}
