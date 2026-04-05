using Microsoft.EntityFrameworkCore;
using FinanceBot.Models;

namespace FinanceBot.Data;

public class FinanceDbContext : DbContext
{
    public DbSet<Transaction> Transactions { get; set; }
    public DbSet<UserCategory> UserCategories { get; set; }

    public FinanceDbContext()
    {
        Database.EnsureCreated();
    }
    protected override void OnConfiguring(DbContextOptionsBuilder o) => o.UseSqlite("Data Source=finance.db");
}