namespace FinanceBot.Models;

public class Transaction
{
    public int Id { get; set; }
    public long UserId { get; set; }
    public decimal Amount { get; set; }
    public string Category { get; set; } = "";
    public string Type { get; set; } = "";
    public string Comment { get; set; } = "";
    public DateTime Date { get; set; }
}

public class TransactionResult
{
    public decimal Amount { get; set; }
    public string Category { get; set; } = "";
    public string Type { get; set; } = "";
    public string Comment { get; set; } = "";
}

public class UserCategory
{
    public int Id { get; set; }
    public long UserId { get; set; } // Чтобы у каждого были свои категории
    public string Name { get; set; } = "";
}