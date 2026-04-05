using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using FinanceBot.Handlers;

// Твой токен
var botToken = "8542975538:AAEoNsgJg7WyssVobMrw_EJXM3Zg36Nkeeo";
var botClient = new TelegramBotClient(botToken);
using var cts = new CancellationTokenSource();

await botClient.SetMyCommands(new[]
{
    new BotCommand { Command = "start", Description = "Главное меню" },
    new BotCommand { Command = "stats", Description = "Аналитика расходов" },
    new BotCommand { Command = "balance", Description = "Текущий баланс" },
    new BotCommand { Command = "reset", Description = "Сброс всех данных" }
});

botClient.StartReceiving(
    UpdateHandler.HandleUpdateAsync,
    UpdateHandler.HandleErrorAsync,
    new ReceiverOptions(),
    cts.Token);

Console.WriteLine("Бот запущен...");
Console.ReadLine();
cts.Cancel();