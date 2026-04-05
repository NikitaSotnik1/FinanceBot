using System.Text;
using System.IO;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using FinanceBot.Models;
using FinanceBot.Services;
using FinanceBot.Data;

namespace FinanceBot.Handlers;

public static class UpdateHandler
{
    private static readonly Dictionary<long, TransactionResult> pendingTransactions = new();
    private static readonly HashSet<long> waitingForBalance = new();
    private static readonly HashSet<long> waitingForCategoryName = new();

    public static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        long chatId = 0;
        if (update.Message != null) chatId = update.Message.Chat.Id;
        else if (update.CallbackQuery != null) chatId = update.CallbackQuery.Message!.Chat.Id;
        if (chatId == 0) return;

        if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery is { } callback)
        {
            await HandleCallback(botClient, callback, chatId);
            return;
        }

        if (update.Message is not { } message) return;

        if (waitingForBalance.Contains(chatId) && decimal.TryParse(message.Text, out decimal newBalance))
        {
            await SetManualBalance(chatId, newBalance);
            waitingForBalance.Remove(chatId);
            await botClient.SendMessage(chatId, $"✅ Баланс обновлен: {newBalance} ₽");
            await ShowMainMenu(botClient, chatId);
            return;
        }

        if (message.Text is { } messageText)
        {
            if (waitingForCategoryName.Contains(chatId))
            {
                if (string.IsNullOrWhiteSpace(messageText) || messageText.Length > 20)
                {
                    await botClient.SendMessage(chatId, "⚠️ Название должно быть коротким (до 20 символов). Попробуйте еще раз:");
                    return;
                }

                await AddCategory(chatId, messageText);
                waitingForCategoryName.Remove(chatId);
                await botClient.SendMessage(chatId, $"✅ Категория <b>{messageText}</b> успешно создана!");
                await ShowCategoriesMenu(botClient, chatId);
                return;
            }

            if (messageText == "/start") { await CheckInitialStatus(botClient, chatId); return; }
            if (messageText == "/reset") { await ResetData(botClient, chatId); return; }
            if (messageText == "📊 Баланс") { await ShowBalance(botClient, chatId); return; }
            if (messageText == "📈 Статистика" || messageText == "/stats") { await ShowStatsMenu(botClient, chatId); return; }
            if (messageText == "📜 История") { await ShowHistory(botClient, chatId); return; }
            if (messageText == "⚙️ Настройки") { await ShowSettings(botClient, chatId); return; }

            var result = await FinanceServices.AnalyzeText(messageText, chatId);
            if (result != null) await AskConfirmation(botClient, chatId, result, false);
        }

        // В HandleUpdateAsync, перед анализом текста
        if (waitingForCategoryName.Contains(chatId))
        {
            await AddCategory(chatId, message.Text!);
            waitingForCategoryName.Remove(chatId);
            await botClient.SendMessage(chatId, $"✅ Категория <b>{message.Text}</b> добавлена!", ParseMode.Html);
            await ShowCategoriesMenu(botClient, chatId);
            return;
        }

        if (message.Voice is { } voice)
        {
            await botClient.SendMessage(chatId, "🎤 Расшифровка...");
            string text = await FinanceServices.RecognizeVoice(botClient, voice);
            var res = await FinanceServices.AnalyzeText(text, chatId);
            if (res != null) await AskConfirmation(botClient, chatId, res, true);
            else await botClient.SendMessage(chatId, $"Не нашел сумму в: \"{text}\"");
        }
    }

    private static async Task HandleCallback(ITelegramBotClient botClient, CallbackQuery callback, long chatId)
    {
        if (callback.Data == "confirm_save" && pendingTransactions.TryGetValue(chatId, out var res))
        {
            await SaveToDb(chatId, res);
            var undoMarkup = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("↩️ Отменить запись", "undo_last"));

            await botClient.EditMessageText(
                chatId: chatId,
                messageId: callback.Message!.MessageId,
                text: $"✅ Записано: <b>{res.Amount} ₽</b> (<i>{res.Category}</i>)",
                parseMode: ParseMode.Html,
                replyMarkup: undoMarkup);

            pendingTransactions.Remove(chatId);
        }
        else if (callback.Data!.StartsWith("set_cat_"))
        {
            string cat = callback.Data.Replace("set_cat_", "");
            if (pendingTransactions.TryGetValue(chatId, out var pRes))
            {
                pRes.Category = cat;
                await SaveToDb(chatId, pRes);
                var undoMarkup = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("↩️ Отменить запись", "undo_last"));

                await botClient.EditMessageText(
                    chatId: chatId,
                    messageId: callback.Message!.MessageId,
                    text: $"✅ Записано в <b>{cat}</b>: <b>{pRes.Amount} ₽</b>",
                    parseMode: ParseMode.Html,
                    replyMarkup: undoMarkup);

                pendingTransactions.Remove(chatId);
            }
        }
        else if (callback.Data == "cancel_save")
        {
            await botClient.EditMessageText(chatId, callback.Message!.MessageId, "❌ Отменено.");
            pendingTransactions.Remove(chatId);
        }
        else if (callback.Data == "set_initial_balance")
        {
            waitingForBalance.Add(chatId);
            await botClient.SendMessage(chatId, "Введите текущий баланс цифрами:");
        }
        else if (callback.Data!.StartsWith("stats_"))
        {
            await GenerateStatistics(botClient, chatId, callback.Data.Replace("stats_", ""), callback.Message!.MessageId);
        }
        else if (callback.Data == "undo_last")
        {
            await DeleteLastTransaction(chatId);
            await botClient.EditMessageText(chatId, callback.Message!.MessageId, "🗑 <b>Запись удалена.</b>", parseMode: ParseMode.Html);
        }
        else if (callback.Data == "add_cat_start")
        {
            waitingForCategoryName.Add(chatId);
            await botClient.SendMessage(chatId, "Введите название для новой категории:");
        }
        else if (callback.Data!.StartsWith("del_cat_"))
        {
            string catName = callback.Data.Replace("del_cat_", "");
            await DeleteCategory(chatId, catName);
            await botClient.EditMessageText(chatId, callback.Message!.MessageId, $"🗑 Категория <b>{catName}</b> удалена. Траты перенесены в «Прочее».", ParseMode.Html);
        }
        // 1. Открытие меню категорий
        else if (callback.Data == "manage_categories")
        {
            await ShowCategoriesMenu(botClient, chatId);
        }
        // 2. Начало процесса добавления
        else if (callback.Data == "add_cat_start")
        {
            waitingForCategoryName.Add(chatId);
            await botClient.SendMessage(chatId, "📝 Введите название для новой категории:");
        }
        // 3. Удаление категории
        else if (callback.Data!.StartsWith("del_cat_"))
        {
            string catName = callback.Data.Replace("del_cat_", "");
            await DeleteCategory(chatId, catName);
            await botClient.EditMessageText(chatId, callback.Message!.MessageId, $"🗑 Категория <b>{catName}</b> удалена. Траты перенесены в «Прочее».", ParseMode.Html);
            await ShowCategoriesMenu(botClient, chatId); // Возвращаемся в меню
        }

        await botClient.AnswerCallbackQuery(callback.Id);
    }

    private static async Task AskConfirmation(ITelegramBotClient botClient, long chatId, TransactionResult res, bool voice)
    {
        pendingTransactions[chatId] = res;

        if (res.Category == "ВЫБОР_КАТЕГОРИИ")
        {
            var categoryButtons = new InlineKeyboardMarkup(new[] {
                new[] { InlineKeyboardButton.WithCallbackData("🛒 Продукты", "set_cat_🛒 Продукты"), InlineKeyboardButton.WithCallbackData("🚕 Транспорт", "set_cat_🚕 Транспорт") },
                new[] { InlineKeyboardButton.WithCallbackData("☕️ Кафе", "set_cat_☕️ Кафе"), InlineKeyboardButton.WithCallbackData("💊 Здоровье", "set_cat_💊 Здоровье") },
                new[] { InlineKeyboardButton.WithCallbackData("🏠 Дом", "set_cat_🏠 Дом"), InlineKeyboardButton.WithCallbackData("🎬 Развлечения", "set_cat_🎬 Развлечения") },
                new[] { InlineKeyboardButton.WithCallbackData("❌ Отмена", "cancel_save") }
            });

            await botClient.SendMessage(chatId, $"💰 Сумма: <b>{res.Amount} ₽</b>\nВыбери категорию:", ParseMode.Html, replyMarkup: categoryButtons);
        }
        else
        {
            var ik = new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("✅ Да", "confirm_save"), InlineKeyboardButton.WithCallbackData("❌ Нет", "cancel_save") } });
            string h = voice ? "🎤 <b>Я услышал:</b>" : "⌨️ <b>Вы написали:</b>";
            string safeComment = res.Comment.Replace("<", "&lt;").Replace(">", "&gt;");

            await botClient.SendMessage(chatId, $"{h}\n<i>\"{safeComment}\"</i>\n\n💰 Сумма: <b>{res.Amount} ₽</b>\n📂 Категория: <b>{res.Category}</b>\nЗаписать?", ParseMode.Html, replyMarkup: ik);
        }
    }

    private static async Task GenerateStatistics(ITelegramBotClient botClient, long chatId, string period, int messageId)
    {
        using var db = new FinanceDbContext();

        // 1. Начальный запрос: только расходы текущего пользователя
        var query = db.Transactions.Where(t => t.UserId == chatId && t.Type == "Расход");

        string periodTitle = "";
        var now = DateTime.Now;

        // 2. Фильтрация по периодам
        if (period == "today")
        {
            query = query.Where(t => t.Date.Date == now.Date);
            periodTitle = "за сегодня";
        }
        else if (period == "month")
        {
            query = query.Where(t => t.Date.Month == now.Month && t.Date.Year == now.Year);
            periodTitle = "за месяц";
        }
        else
        {
            periodTitle = "за всё время";
        }

        var expenses = query.ToList();

        // 3. Если данных нет — уведомляем пользователя
        if (!expenses.Any())
        {
            await botClient.EditMessageText(chatId, messageId, $"За период \"{periodTitle}\" расходов не найдено. 🤷‍♂️");
            return;
        }

        // 4. Группировка данных для графика
        decimal total = expenses.Sum(e => e.Amount);
        var grouped = expenses.GroupBy(e => e.Category)
                              .Select(g => (
                                  Category: g.Key,
                                  Sum: g.Sum(s => s.Amount),
                                  Percent: (double)(g.Sum(s => s.Amount) / total) * 100
                              ))
                              .OrderByDescending(g => g.Sum)
                              .ToList();

        // 5. Генерация изображения через наш новый сервис
        byte[] imageBytes = ChartService.GenerateExpenseChart($"Аналитика {periodTitle}", grouped);

        // 6. Удаляем старое сообщение с кнопками выбора и отправляем фото
        await botClient.DeleteMessage(chatId, messageId);

        using var ms = new MemoryStream(imageBytes);

        string captionText = $"📊 <b>Статистика {periodTitle}</b>\n💰 Итого потрачено: <b>{total:N1} ₽</b>";

        await botClient.SendPhoto(
            chatId: chatId,
            photo: InputFile.FromStream(ms, "stats.png"),
            caption: captionText,
            parseMode: ParseMode.Html 
        );
    }

    //private static async Task AskConfirmation(ITelegramBotClient botClient, long chatId, TransactionResult res, bool voice)
    //{
    //    pendingTransactions[chatId] = res;
    //    var ik = new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("✅ Да", "confirm_save"), InlineKeyboardButton.WithCallbackData("❌ Нет", "cancel_save") } });
    //    string h = voice ? "🎤 **Я услышал:**" : "⌨️ **Вы написали:**";
    //    await botClient.SendMessage(chatId, $"{h}\n_\"{res.Comment}\"_\n\n💰 Сумма: **{res.Amount} ₽**\n📂 Категория: **{res.Category}**\nЗаписать?", ParseMode.Markdown, replyMarkup: ik);
    //}

    //private static async Task AskConfirmation(ITelegramBotClient botClient, long chatId, TransactionResult res, bool voice)
    //{
    //    pendingTransactions[chatId] = res;

    //    // Сравнение должно быть точным
    //    if (res.Category == "ВЫБОР_КАТЕГОРИИ")
    //    {
    //        var categoryButtons = new InlineKeyboardMarkup(new[] {
    //        new[] { InlineKeyboardButton.WithCallbackData("🛒 Продукты", "set_cat_🛒 Продукты"), InlineKeyboardButton.WithCallbackData("🚕 Транспорт", "set_cat_🚕 Транспорт") },
    //        new[] { InlineKeyboardButton.WithCallbackData("☕️ Кафе", "set_cat_☕️ Кафе"), InlineKeyboardButton.WithCallbackData("💊 Здоровье", "set_cat_💊 Здоровье") },
    //        new[] { InlineKeyboardButton.WithCallbackData("🏠 Дом", "set_cat_🏠 Дом"), InlineKeyboardButton.WithCallbackData("🎬 Развлечения", "set_cat_🎬 Развлечения") },
    //        new[] { InlineKeyboardButton.WithCallbackData("❌ Отмена", "cancel_save") }
    //    });

    //        await botClient.SendMessage(chatId, $"💰 Сумма: <b>{res.Amount} ₽</b>\nВыбери категорию:", ParseMode.Html, replyMarkup: categoryButtons);
    //    }
    //    else
    //    {
    //        var ik = new InlineKeyboardMarkup(new[] {
    //        new[] { InlineKeyboardButton.WithCallbackData("✅ Да", "confirm_save"), InlineKeyboardButton.WithCallbackData("❌ Нет", "cancel_save") }
    //    });

    //        string header = voice ? "🎤 <b>Я услышал:</b>" : "⌨️ <b>Вы написали:</b>";
    //        string safeComment = res.Comment.Replace("<", "&lt;").Replace(">", "&gt;");

    //        await botClient.SendMessage(chatId, $"{header}\n<i>\"{safeComment}\"</i>\n\n💰 Сумма: <b>{res.Amount} ₽</b>\n📂 Категория: <b>{res.Category}</b>\nЗаписать?", ParseMode.Html, replyMarkup: ik);
    //    }
    //}

    //private static async Task AskConfirmation(ITelegramBotClient botClient, long chatId, TransactionResult res, bool voice)
    //{
    //    pendingTransactions[chatId] = res;
    //    var ik = new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("✅ Да", "confirm_save"), InlineKeyboardButton.WithCallbackData("❌ Нет", "cancel_save") } });
    //    string h = voice ? "🎤 **Я услышал:**" : "⌨️ **Вы написали:**";
    //    await botClient.SendMessage(chatId, $"{h}\n_\"{res.Comment}\"_\n\n💰 Сумма: **{res.Amount} ₽**\n📂 Категория: **{res.Category}**\nЗаписать?", ParseMode.Html, replyMarkup: ik);
    //}
    public static async Task ShowMainMenu(ITelegramBotClient b, long id)
    {
        var rk = new ReplyKeyboardMarkup(new[] { new[] { new KeyboardButton("📊 Баланс"), new KeyboardButton("📈 Статистика") }, new[] { new KeyboardButton("📜 История"), new KeyboardButton("⚙️ Настройки") } }) { ResizeKeyboard = true };
        await b.SendMessage(id, "Главное меню:", replyMarkup: rk);
    }

    private static async Task ShowBalance(ITelegramBotClient b, long id)
    {
        using var db = new FinanceDbContext();
        var bal = db.Transactions.Where(t => t.UserId == id).ToList().Sum(t => t.Type == "Доход" ? t.Amount : -t.Amount);
        await b.SendMessage(id, $"💰 Текущий баланс: **{bal} ₽**", ParseMode.Html);
    }

    private static async Task ShowHistory(ITelegramBotClient b, long id)
    {
        using var db = new FinanceDbContext();
        var last = db.Transactions.Where(t => t.UserId == id).OrderByDescending(t => t.Date).Take(5).ToList();
        string h = last.Count == 0 ? "История пуста." : "📜 Последние операции:\n" + string.Join("\n", last.Select(t => $"{t.Date:dd.MM} | {t.Amount}₽ | {t.Category}"));
        await b.SendMessage(id, h);
    }

    private static async Task ShowStatsMenu(ITelegramBotClient b, long id)
    {
        var ik = new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("📅 Сегодня", "stats_today") }, new[] { InlineKeyboardButton.WithCallbackData("🗓 Месяц", "stats_month") }, new[] { InlineKeyboardButton.WithCallbackData("♾ Всё время", "stats_all") } });
        await b.SendMessage(id, "Выберите период:", replyMarkup: ik);
    }

    private static async Task ShowSettings(ITelegramBotClient b, long id)
    {

        var ik = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("📈 Корректировка баланса", "set_initial_balance") },
            new[] { InlineKeyboardButton.WithCallbackData("📁 Управление категориями", "manage_categories") }
        });
        await b.SendMessage(id, "<b>Настройки:</b>", ParseMode.Html, replyMarkup: ik);
    }

    private static async Task CheckInitialStatus(ITelegramBotClient b, long id)
    {
        using var db = new FinanceDbContext();

        if (!db.UserCategories.Any(c => c.UserId == id))
        {
            // Базовый набор для новичка
            db.UserCategories.AddRange(new List<UserCategory>
            {
                new UserCategory { UserId = id, Name = "🛒 Продукты" },
                new UserCategory { UserId = id, Name = "🚕 Транспорт" },
                new UserCategory { UserId = id, Name = "☕️ Кафе" }
            });
            await db.SaveChangesAsync();
        }

        // Текст с описанием возможностей
        string welcomeText = "👋 <b>Привет! Я твой персональный финансовый помощник.</b>\n\n" +
                         "Вот что я умею:\n" +
                         "🎤 <b>Голосовой ввод:</b> Просто продиктуй трату, например: «Купил кофе за 250 рублей».\n" +
                         "⌨/ <b>Текстовый ввод:</b> Напиши сообщение в свободном формате.\n" +
                         "📊 <b>Аналитика:</b> Смотри статистику расходов по категориям за сегодня, месяц или всё время.\n" +
                         "💰 <b>Контроль:</b> Следи за актуальным балансом в реальном времени.\n\n" +
                         "Для начала работы давай установим твой текущий баланс:";

        if (!db.Transactions.Any(t => t.UserId == id))
        {
            var ik = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("💰 Задать стартовый баланс", "set_initial_balance"));
            await b.SendMessage(id, welcomeText, ParseMode.Html, replyMarkup: ik);
        }
        else
        {
            // Если пользователь уже есть, просто выводим краткое приветствие и меню
            await b.SendMessage(id, "С возвращением! Я готов записывать твои расходы.", ParseMode.Html);
            await ShowMainMenu(b, id);
        }
    }

    private static async Task ResetData(ITelegramBotClient b, long id)
    {
        using var db = new FinanceDbContext();
        db.Transactions.RemoveRange(db.Transactions.Where(t => t.UserId == id));
        await db.SaveChangesAsync();
        await b.SendMessage(id, "💥 Данные удалены.");
    }

    private static async Task SetManualBalance(long id, decimal val)
    {
        using var db = new FinanceDbContext();
        var cur = db.Transactions.Where(t => t.UserId == id).ToList().Sum(t => t.Type == "Доход" ? t.Amount : -t.Amount);
        decimal diff = val - cur;
        db.Transactions.Add(new Transaction { UserId = id, Amount = Math.Abs(diff), Category = "⚙️ Корректировка", Type = diff >= 0 ? "Доход" : "Расход", Comment = "Правка", Date = DateTime.Now });
        await db.SaveChangesAsync();
    }

    private static async Task SaveToDb(long id, TransactionResult res)
    {
        using var db = new FinanceDbContext();
        db.Transactions.Add(new Transaction { UserId = id, Amount = res.Amount, Category = res.Category, Type = res.Type, Comment = res.Comment, Date = DateTime.Now });
        await db.SaveChangesAsync();
    }

    public static Task HandleErrorAsync(ITelegramBotClient b, Exception e, CancellationToken c) { Console.WriteLine(e); return Task.CompletedTask; }

    private static async Task DeleteLastTransaction(long userId)
    {
        using var db = new FinanceDbContext();

        // Ищем последнюю операцию именно этого пользователя
        var lastEntry = db.Transactions
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.Date)
            .FirstOrDefault();

        if (lastEntry != null)
        {
            db.Transactions.Remove(lastEntry);
            await db.SaveChangesAsync();
        }
    }

    private static async Task ShowCategoriesMenu(ITelegramBotClient b, long id)
    {
        using var db = new FinanceDbContext();
        var userCats = db.UserCategories.Where(c => c.UserId == id).ToList();

        var buttons = new List<InlineKeyboardButton[]>();
        foreach (var cat in userCats)
        {
            buttons.Add(new[] {
            InlineKeyboardButton.WithCallbackData(cat.Name, "ignore"),
            InlineKeyboardButton.WithCallbackData("🗑 Удалить", $"del_cat_{cat.Name}")
        });
        }
        buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("➕ Добавить новую", "add_cat_start") });

        await b.SendMessage(id, "<b>Управление категориями:</b>\n<i>При удалении категории все связанные с ней траты перейдут в «Прочее».</i>", ParseMode.Html, replyMarkup: new InlineKeyboardMarkup(buttons));
    }

    private static async Task DeleteCategory(long userId, string catName)
    {
        using var db = new FinanceDbContext();

        // ПЕРЕНОС ТРАНЗАКЦИЙ (Твоя главная фишка для диплома!)
        var transactionsToUpdate = db.Transactions.Where(t => t.UserId == userId && t.Category == catName).ToList();
        foreach (var t in transactionsToUpdate)
        {
            t.Category = "❓ Прочее";
        }

        // Удаление самой категории
        var cat = db.UserCategories.FirstOrDefault(c => c.UserId == userId && c.Name == catName);
        if (cat != null) db.UserCategories.Remove(cat);

        await db.SaveChangesAsync();
    }

    private static async Task AddCategory(long userId, string name)
    {
        using var db = new FinanceDbContext();
        db.UserCategories.Add(new UserCategory { UserId = userId, Name = name });
        await db.SaveChangesAsync();
    }
}
