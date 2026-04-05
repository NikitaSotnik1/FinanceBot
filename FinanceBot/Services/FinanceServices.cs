using FinanceBot.Data;
using FinanceBot.Models;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Telegram.Bot;
using Telegram.Bot.Types;
using Whisper.net;

namespace FinanceBot.Services;

public static class FinanceServices
{
    public static async Task<string> RecognizeVoice(ITelegramBotClient botClient, Voice voice)
    {
        string ogg = "voice.ogg"; string wav = "voice.wav";
        var fileInfo = await botClient.GetFile(voice.FileId);
        using (var fs = System.IO.File.OpenWrite(ogg)) await botClient.DownloadFile(fileInfo.FilePath!, fs);
        var ffmpeg = Process.Start(new ProcessStartInfo { FileName = "ffmpeg.exe", Arguments = $"-y -i {ogg} -ar 16000 -ac 1 {wav}", CreateNoWindow = true });
        await ffmpeg!.WaitForExitAsync();
        using var factory = WhisperFactory.FromPath("ggml-base.bin");
        using var processor = factory.CreateBuilder().WithLanguage("ru").Build();
        using var wavStream = System.IO.File.OpenRead(wav);
        string text = "";
        await foreach (var segment in processor.ProcessAsync(wavStream)) text += segment.Text;
        return text;
    }

    //public static async Task<TransactionResult?> AnalyzeText(string text)
    //{
    //    text = text.ToLower();
    //    string cleaned = Regex.Replace(text, @"(\d)[.,](\d)", "$1$2");
    //    var matches = Regex.Matches(cleaned, @"\d+");
    //    if (matches.Count == 0) return null;
    //    decimal amt = matches.Select(m => decimal.Parse(m.Value)).Max();

    //    if (amt < 1000)
    //    {
    //        bool hasK = text.Contains("тыс") || text.Contains("тыщ") || text.Contains(" тысяч") || text.Contains(" к ");
    //        if (hasK) amt *= 1000;
    //    }

    //    var res = new TransactionResult { Amount = amt, Comment = text, Type = "Расход", Category = "❓ Прочее" };

    //    // Улучшенный классификатор категорий
    //    if (text.Contains("зарплат") || text.Contains("доход") || text.Contains("пришло") || text.Contains("перевод от"))
    //    {
    //        res.Type = "Доход";
    //        res.Category = "💰 Доходы";
    //    }
    //    else if (ContainsAny(text, "продукт", "еда", "хлеб", "купил", "пятерочк", "магнит", "вкусвилл", "ашан"))
    //    {
    //        res.Category = "🛒 Продукты";
    //    }
    //    else if (ContainsAny(text, "такси", "авто", "бензин", "машин", "метро", "автобус", "парковк", "проезд"))
    //    {
    //        res.Category = "🚕 Транспорт";
    //    }
    //    else if (ContainsAny(text, "кофе", "кафе", "ресторан", "бургер", "пицца", "макдак", "завтрак", "обед"))
    //    {
    //        res.Category = "☕️ Кафе";
    //    }
    //    else if (ContainsAny(text, "аптек", "врач", "таблетк", "больниц", "здоров"))
    //    {
    //        res.Category = "💊 Здоровье";
    //    }
    //    else if (ContainsAny(text, "кино", "фильм", "подписк", "игры", "театр", "развлеч"))
    //    {
    //        res.Category = "🎬 Развлечения";
    //    }
    //    else if (ContainsAny(text, "квартир", "аренд", "свет", "вода", "коммунал", "домашние"))
    //    {
    //        res.Category = "🏠 Дом";
    //    }

    //    return res;
    //}

    //public static async Task<TransactionResult?> AnalyzeText(string text)
    //{
    //    if (string.IsNullOrWhiteSpace(text)) return null;

    //    text = text.ToLower().Trim();

    //    // 1. Очистка для суммы (5.563 -> 5563)
    //    string cleanedForAmount = Regex.Replace(text, @"(\d)[.,](\d)", "$1$2");
    //    var matches = Regex.Matches(cleanedForAmount, @"\d+");
    //    if (matches.Count == 0) return null;

    //    decimal amt = matches.Select(m => decimal.Parse(m.Value)).Max();
    //    if (amt < 1000 && (text.Contains("тыс") || text.Contains("тыщ") || text.Contains(" к "))) amt *= 1000;

    //    // 2. ПРОВЕРКА: Это только число или есть слова?
    //    // Убираем всё, кроме букв. Если букв нет — значит, только цифры.
    //    string onlyLetters = Regex.Replace(text, @"[\d.,\s]", "");
    //    bool isOnlyNumbers = string.IsNullOrEmpty(onlyLetters);

    //    // 3. Устанавливаем категорию
    //    string category = isOnlyNumbers ? "ВЫБОР_КАТЕГОРИИ" : "❓ Прочее";
    //    var res = new TransactionResult { Amount = amt, Comment = text, Type = "Расход", Category = category };

    //    // 4. Если есть текст — ищем категории
    //    if (!isOnlyNumbers)
    //    {
    //        if (ContainsAny(text, "зарплат", "доход", "пришло")) { res.Type = "Доход"; res.Category = "💰 Доходы"; }
    //        else if (ContainsAny(text, "продукт", "еда", "хлеб", "купил", "пятерочк", "магнит", "вкусвилл")) res.Category = "🛒 Продукты";
    //        else if (ContainsAny(text, "такси", "авто", "бензин", "машин", "метро", "автобус")) res.Category = "🚕 Транспорт";
    //        else if (ContainsAny(text, "кофе", "кафе", "ресторан", "бургер", "пицца", "макдак")) res.Category = "☕️ Кафе";
    //        else if (ContainsAny(text, "аптек", "врач", "таблетк", "больниц", "здоровье")) res.Category = "💊 Здоровье";
    //        else if (ContainsAny(text, "кино", "фильм", "подписк", "игры", "театр", "развлеч")) res.Category = "🎬 Развлечения";
    //        else if (ContainsAny(text, "квартир", "аренд", "свет", "вода", "коммунал")) res.Category = "🏠 Дом";
    //    }

    //    return res;
    //}

    // Вспомогательный метод для чистоты кода

    public static async Task<TransactionResult?> AnalyzeText(string text, long userId)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        string originalText = text.Trim();
        string lowerText = originalText.ToLower();

        // 1. Поиск суммы (остается как было)
        string cleanedForAmount = Regex.Replace(lowerText, @"(\d)[.,](\d)", "$1$2");
        var matches = Regex.Matches(cleanedForAmount, @"\d+");
        if (matches.Count == 0) return null;

        decimal amt = matches.Select(m => decimal.Parse(m.Value)).Max();
        if (amt < 1000 && (lowerText.Contains("тыс") || lowerText.Contains("тыщ") || lowerText.Contains(" к "))) amt *= 1000;

        bool hasLetters = Regex.IsMatch(lowerText, @"[а-яА-Яa-zA-Z]");

        // По умолчанию ставим "Прочее" или маркер выбора
        string category = !hasLetters ? "ВЫБОР_КАТЕГОРИИ" : "❓ Прочее";
        string type = "Расход";

        // 2. ДИНАМИЧЕСКИЙ ПОИСК КАТЕГОРИИ В БАЗЕ
        if (hasLetters)
        {
            using var db = new FinanceDbContext();

            // Сначала проверяем системную логику доходов
            if (ContainsAny(lowerText, "зарплат", "доход", "пришло", "перевод"))
            {
                type = "Доход";
                category = "💰 Доходы";
            }
            else
            {
                // Ищем совпадения среди личных категорий пользователя
                var userCats = db.UserCategories.Where(c => c.UserId == userId).ToList();
                foreach (var cat in userCats)
                {
                    // Если название категории (например "Кофе") есть в тексте "купил кофе"
                    if (lowerText.Contains(cat.Name.ToLower()))
                    {
                        category = cat.Name;
                        break;
                    }
                }
            }
        }

        return new TransactionResult { Amount = amt, Comment = originalText, Type = type, Category = category };
    }
    private static bool ContainsAny(string text, params string[] keywords)
    {
        return keywords.Any(k => text.Contains(k));
    }
}