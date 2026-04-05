//using Telegram.Bot;
//using Telegram.Bot.Polling;
//using Telegram.Bot.Types;
//using FinanceBot.Handlers;

//// Твой токен
//var botToken = "8542975538:AAEoNsgJg7WyssVobMrw_EJXM3Zg36Nkeeo";
//var botClient = new TelegramBotClient(botToken);
//using var cts = new CancellationTokenSource();

//await botClient.SetMyCommands(new[]
//{
//    new BotCommand { Command = "start", Description = "Главное меню" },
//    new BotCommand { Command = "stats", Description = "Аналитика расходов" },
//    new BotCommand { Command = "balance", Description = "Текущий баланс" },
//    new BotCommand { Command = "reset", Description = "Сброс всех данных" }
//});

//botClient.StartReceiving(
//    UpdateHandler.HandleUpdateAsync,
//    UpdateHandler.HandleErrorAsync,
//    new ReceiverOptions(),
//    cts.Token);

//Console.WriteLine("Бот запущен...");
//Console.ReadLine();
//cts.Cancel();

using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using FinanceBot.Handlers;
using Microsoft.Extensions.Configuration; // Добавили для работы с конфигом

// 1. Настройка конфигурации
var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json")
    .Build();

// 2. Получение токена из файла
var botToken = config["BotSettings:Token"];

// Проверка, что токен считался (поможет при отладке)
if (string.IsNullOrEmpty(botToken))
{
    Console.WriteLine("Ошибка: Токен не найден в appsettings.json!");
    return;
}

var botClient = new TelegramBotClient(botToken);
using var cts = new CancellationTokenSource();

// Установка команд
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