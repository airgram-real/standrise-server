using ProjectRework.Web;
using System.Text.Json;

// Веб-магазин StandRework (Mini App). Игровой сервер не подключается и не импортируется —
// это отдельный процесс, и уронить игру он не может.

string configPath = Path.Combine(AppContext.BaseDirectory, "web.settings.json");
var cfg = WebConfig.Load(configPath);

ShopCatalog.Configure(Path.Combine(AppContext.BaseDirectory, "catalog.json"));
LinkStore.Configure(cfg.LinkedUsersPath);
AdminStore.Configure(cfg.BotRolesPath);

string botToken = cfg.ReadBotToken();
var bot = new BotApi(botToken);
var game = new GameApi(cfg.GameApiUrl, botToken);

// Ключ, которым бот просит у сайта ссылку в админку. Тот же общий секрет,
// что и у игрового админ-API: SHA-256 от токена бота, второй копии нет.
string botSharedKey = string.IsNullOrEmpty(botToken)
    ? ""
    : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(botToken))).ToLowerInvariant();

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddSimpleConsole(o => o.SingleLine = true);
builder.WebHost.UseUrls($"http://{cfg.BindAddress}:{cfg.Port}");

var app = builder.Build();

// Ответы API не кэшируются нигде: иначе прокси впереди (у нас Cloudflare)
// отдаёт устаревшую витрину и устаревший баланс игрока.
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api"))
    {
        ctx.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        ctx.Response.Headers["Pragma"] = "no-cache";

        // Тело читают по два раза: сначала проверка входа, потом сам обработчик.
        // Без этого поток неперематываемый, и вторая попытка возвращала пустоту —
        // именно из-за этого вход по ID отвечал «введи свой игровой ID» на любой ввод.
        ctx.Request.EnableBuffering();
    }
    await next();
});

// Статика собранного фронтенда.
if (Directory.Exists(cfg.SiteRoot))
{
    var files = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(cfg.SiteRoot);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = files });
}
else
{
    Console.WriteLine($"[Web] нет папки сайта {cfg.SiteRoot} — отдаю только API.");
}

// ---------- Кто пришёл ----------

// Три способа войти, в порядке доверия:
//   1) Mini App Telegram — подпись проверена, знаем telegram id;
//   2) сессия админки, выданная по одноразовой ссылке из бота;
//   3) просто введённый игровой ID — этого хватает, чтобы купить и ввести промокод,
//      но НЕ хватает для админки: иначе админом станет любой, кто наберёт чужой номер.
async Task<Caller> Auth(HttpContext ctx)
{
    ctx.Request.EnableBuffering();

    string initData = ctx.Request.Headers["X-Telegram-Init-Data"].ToString();
    string session = ctx.Request.Headers["X-Admin-Session"].ToString();
    string typedId = ctx.Request.Headers["X-Player-Id"].ToString();

    if (ctx.Request.HasJsonContentType())
    {
        try
        {
            ctx.Request.Body.Position = 0;
            using var reader = new StreamReader(ctx.Request.Body, leaveOpen: true);
            string raw = await reader.ReadToEndAsync();
            ctx.Request.Body.Position = 0;
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (string.IsNullOrEmpty(initData) && root.TryGetProperty("initData", out var e1))
                initData = e1.GetString() ?? "";
            if (string.IsNullOrEmpty(session) && root.TryGetProperty("adminSession", out var e2))
                session = e2.GetString() ?? "";
            if (string.IsNullOrEmpty(typedId) && root.TryGetProperty("playerId", out var e3))
                typedId = e3.GetString() ?? "";
        }
        catch { }
    }

    // ID, введённый на самом сайте, важнее привязки из бота: игрок мог указать
    // другой аккаунт прямо здесь, и покупка должна уйти именно на него.
    string typed = (typedId ?? "").Trim();

    if (!string.IsNullOrWhiteSpace(initData))
    {
        if (TelegramMiniApp.TryValidate(initData, botToken, cfg.InitDataMaxAgeSeconds,
                out var tgUser, out string err))
        {
            string linked = LinkStore.GetPlayerId(tgUser.Id);
            // В Mini App покупки только после проверки кода в игре (привязка в боте).
            string player = string.IsNullOrEmpty(typed) ? linked : typed;
            if (string.IsNullOrEmpty(typed) && string.IsNullOrEmpty(linked))
                player = "";
            return new Caller(tgUser.Id, tgUser.Username, tgUser.FirstName, true,
                player, AdminStore.IsAdmin(tgUser.Id));
        }
        // Раньше здесь был отказ на весь сайт. Теперь просто пишем причину в лог
        // и пускаем как обычного гостя: магазин работает по введённому ID.
        Console.WriteLine($"[Web] initData не прошла проверку: {err}");
    }

    long adminTg = AdminSessions.Resolve(session);
    if (adminTg != 0)
    {
        string linked = LinkStore.GetPlayerId(adminTg);
        return new Caller(adminTg, "", "", true,
            string.IsNullOrEmpty(typed) ? linked : typed,
            AdminStore.IsAdmin(adminTg));
    }

    return new Caller(0, "", "", false, typed, false);
}

async Task<System.Text.Json.JsonElement> BodyOf(HttpContext ctx)
{
    try
    {
        ctx.Request.EnableBuffering();
        ctx.Request.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
        return doc.RootElement.Clone();
    }
    catch { return default; }
}

string Field(System.Text.Json.JsonElement el, string name)
{
    if (el.ValueKind != JsonValueKind.Object) return "";
    if (!el.TryGetProperty(name, out var v)) return "";
    return v.ValueKind switch
    {
        JsonValueKind.String => v.GetString() ?? "",
        JsonValueKind.Number => v.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => ""
    };
}

long ReadLong(System.Text.Json.JsonElement el, string name)
{
    if (el.ValueKind != JsonValueKind.Object) return 0;
    if (!el.TryGetProperty(name, out var v)) return 0;
    return v.ValueKind switch
    {
        JsonValueKind.Number => v.TryGetInt64(out long n) ? n : (long)v.GetDouble(),
        JsonValueKind.String => long.TryParse(v.GetString(), out long p) ? p : 0,
        _ => 0
    };
}

// Ответ игрового сервера пересылаем как есть — свой JSON не выдумываем.
async Task Relay(HttpContext ctx, (int status, string json) r)
{
    ctx.Response.StatusCode = r.status;
    ctx.Response.ContentType = "application/json; charset=utf-8";
    await ctx.Response.WriteAsync(r.json);
}

// Карточка игрока из игрового сервера: ник, аватарка, playerId.
async Task<(bool ok, string playerId, string uid, string name, string avatar, string error)> Card(
    string id, CancellationToken ct)
{
    var r = await game.GetAsync("/api/admin/player", new Dictionary<string, string> { ["id"] = id }, ct);
    if (r.status != 200)
    {
        string msg = "игрок не найден";
        try
        {
            using var d = JsonDocument.Parse(r.json);
            if (d.RootElement.TryGetProperty("error", out var e)) msg = e.GetString() ?? msg;
        }
        catch { }
        return (false, "", "", "", "", msg);
    }
    try
    {
        using var d = JsonDocument.Parse(r.json);
        var e = d.RootElement;
        return (true, Field(e, "playerId"), Field(e, "uid"), Field(e, "name"), Field(e, "avatar"), "");
    }
    catch { return (false, "", "", "", "", "игровой сервер ответил непонятно"); }
}

// ---------- API ----------

app.MapGet("/api/health", () => Results.Json(new
{
    ok = true,
    botToken = bot.HasToken,
    site = Directory.Exists(cfg.SiteRoot),
    links = LinkStore.Count,
    gameApi = game.HasKey,
}));

// Витрина открыта без авторизации: цены видно и снаружи Telegram.
app.MapGet("/api/catalog", () =>
{
    var c = ShopCatalog.Current();
    return Results.Json(new { ok = true, gold = c.Gold, pass = c.Pass });
});

app.MapGet("/api/upgrader/catalog", async (HttpContext ctx) =>
{
    var r = await game.GetAsync("/api/market/upgrader",
        new Dictionary<string, string> { ["limit"] = "400" }, ctx.RequestAborted);
    ctx.Response.StatusCode = r.status;
    ctx.Response.ContentType = "application/json";
    await ctx.Response.WriteAsync(r.json);
});

app.MapGet("/api/upgrader/inventory", async (HttpContext ctx) =>
{
    var me = await Auth(ctx);
    if (string.IsNullOrEmpty(me.PlayerId))
    {
        ctx.Response.StatusCode = 401;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync("{\"ok\":false,\"error\":\"привяжи игровой ID\",\"code\":\"not_linked\"}");
        return;
    }
    var r = await game.GetAsync("/api/market/upgrader/inventory",
        new Dictionary<string, string> { ["playerId"] = me.PlayerId }, ctx.RequestAborted);
    ctx.Response.StatusCode = r.status;
    ctx.Response.ContentType = "application/json";
    await ctx.Response.WriteAsync(r.json);
});

app.MapPost("/api/upgrader/bet", async (HttpContext ctx) =>
{
    var me = await Auth(ctx);
    if (string.IsNullOrEmpty(me.PlayerId))
    {
        ctx.Response.StatusCode = 401;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync("{\"ok\":false,\"error\":\"привяжи игровой ID\",\"code\":\"not_linked\"}");
        return;
    }
    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();
    var r = await game.PostRawAsync("/api/market/upgrader/bet", body,
        new Dictionary<string, string> { ["playerId"] = me.PlayerId }, ctx.RequestAborted);
    ctx.Response.StatusCode = r.status;
    ctx.Response.ContentType = "application/json";
    await ctx.Response.WriteAsync(r.json);
});

// Кто я. Ничего не требует: без Telegram вернёт гостя, сайт покажет ввод ID.
app.MapPost("/api/me", async (HttpContext ctx) =>
{
    var me = await Auth(ctx);

    string name = "", avatar = "", uid = "";
    long gold = 0, silver = 0;
    int level = 0;
    object allies = null, competitive = null, medal = null, battlePass = null;
    if (!string.IsNullOrEmpty(me.PlayerId))
    {
        var r = await game.GetAsync("/api/admin/player",
            new Dictionary<string, string> { ["id"] = me.PlayerId }, ctx.RequestAborted);
        if (r.status == 200)
        {
            try
            {
                using var d = JsonDocument.Parse(r.json);
                var e = d.RootElement;
                name = Field(e, "name");
                avatar = Field(e, "avatar");
                uid = Field(e, "uid");
                gold = ReadLong(e, "gold");
                silver = ReadLong(e, "silver");
                level = (int)ReadLong(e, "level");
                if (e.TryGetProperty("allies", out var a)) allies = JsonSerializer.Deserialize<object>(a.GetRawText());
                if (e.TryGetProperty("competitive", out var c)) competitive = JsonSerializer.Deserialize<object>(c.GetRawText());
                if (e.TryGetProperty("medal", out var m) && m.ValueKind != JsonValueKind.Null)
                    medal = JsonSerializer.Deserialize<object>(m.GetRawText());
                if (e.TryGetProperty("battlePass", out var bp) && bp.ValueKind != JsonValueKind.Null)
                    battlePass = JsonSerializer.Deserialize<object>(bp.GetRawText());
            }
            catch { }
        }
    }

    string botLinkedId = me.Verified && me.TelegramId > 0 ? LinkStore.GetPlayerId(me.TelegramId) : "";
    bool needsTelegramVerify = me.Verified && me.TelegramId > 0 && !me.IsAdmin
        && string.IsNullOrEmpty(botLinkedId);

    await ctx.Response.WriteAsJsonAsync(new
    {
        ok = true,
        telegramId = me.TelegramId,
        username = me.Username,
        firstName = me.FirstName,
        verified = me.Verified,
        playerId = me.PlayerId,
        uid,
        linked = !string.IsNullOrEmpty(me.PlayerId),
        telegramLinked = !string.IsNullOrEmpty(botLinkedId),
        needsTelegramVerify,
        name,
        avatar,
        isAdmin = me.IsAdmin,
        gold,
        silver,
        level,
        allies,
        competitive,
        medal,
        battlePass,
    });
});

// Вход по игровому ID: проверяем, что такой игрок есть, и возвращаем ник с аватаркой.
app.MapPost("/api/link", async (HttpContext ctx) =>
{
    string id = Field(await BodyOf(ctx), "id").Trim();
    if (string.IsNullOrEmpty(id))
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "Введи свой игровой ID" });
        return;
    }

    var c = await Card(id, ctx.RequestAborted);
    if (!c.ok)
    {
        ctx.Response.StatusCode = 404;
        await ctx.Response.WriteAsJsonAsync(new { ok = false, error = c.error });
        return;
    }

    await ctx.Response.WriteAsJsonAsync(new
    {
        ok = true,
        playerId = c.playerId,
        uid = c.uid,
        name = c.name,
        avatar = c.avatar,
    });
});

// Поиск игрока по ID / UID / нику — для подарка и для админки.
app.MapPost("/api/lookup", async (HttpContext ctx) =>
{
    var me = await Auth(ctx);
    string id = Field(await BodyOf(ctx), "id").Trim();
    if (string.IsNullOrEmpty(id))
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "Введи ID игрока" });
        return;
    }

    var r = await game.GetAsync("/api/admin/player",
        new Dictionary<string, string> { ["id"] = id }, ctx.RequestAborted);
    if (r.status != 200) { await Relay(ctx, r); return; }
    if (me.IsAdmin) { await Relay(ctx, r); return; }

    // Обычному пользователю — только ник и аватарка, без голды и банов.
    try
    {
        using var doc = JsonDocument.Parse(r.json);
        var e = doc.RootElement;
        await ctx.Response.WriteAsJsonAsync(new
        {
            ok = true,
            playerId = Field(e, "playerId"),
            uid = Field(e, "uid"),
            name = Field(e, "name"),
            avatar = Field(e, "avatar"),
            online = Field(e, "online") == "true",
        });
    }
    catch { await Relay(ctx, r); }
});

// Ссылка на оплату звёздами.
app.MapPost("/api/invoice", async (HttpContext ctx) =>
{
    var me = await Auth(ctx);
    var body = await BodyOf(ctx);

    var product = ShopCatalog.Current().Find(Field(body, "productId"));
    if (product == null)
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "Товар не найден" });
        return;
    }

    // Подарок другу: платит покупатель, начисление идёт указанному игроку.
    // Отдельной выдачи не появляется — в payload просто уходит его playerId.
    string playerId = me.PlayerId;
    bool isGift = false;
    string giftName = "";

    string giftTo = Field(body, "targetId").Trim();
    if (!string.IsNullOrEmpty(giftTo))
    {
        var c = await Card(giftTo, ctx.RequestAborted);
        if (!c.ok)
        {
            ctx.Response.StatusCode = 404;
            await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "Игрок для подарка не найден" });
            return;
        }
        playerId = c.playerId;
        giftName = string.IsNullOrEmpty(c.name) ? c.uid : c.name;
        isGift = true;
    }

    if (string.IsNullOrEmpty(playerId))
    {
        ctx.Response.StatusCode = 409;
        await ctx.Response.WriteAsJsonAsync(new
        {
            ok = false,
            code = me.Verified && me.TelegramId > 0 ? "needs_verify" : "not_linked",
            error = me.Verified && me.TelegramId > 0
                ? "Подтверди игровой ID кодом из личных сообщений в игре."
                : "Сначала укажи свой игровой ID."
        });
        return;
    }

    // payload тот же, что выставляет бот, — выдачу делает он же, своим кодом.
    string payload = product.BuildPayload(playerId);
    var (ok, link, error) = await bot.CreateInvoiceLinkAsync(
        product.Title, product.BuildDescription(playerId), payload, product.Stars, ctx.RequestAborted);

    if (!ok)
    {
        Console.WriteLine($"[Web] инвойс не выпущен для {playerId}, товар={product.Id}: {error}");
        ctx.Response.StatusCode = 502;
        await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "Telegram не выдал счёт: " + error });
        return;
    }

    Console.WriteLine($"[Web] инвойс tg={me.TelegramId} player={playerId} товар={product.Id} звёзд={product.Stars}");
    await ctx.Response.WriteAsJsonAsync(new
    {
        ok = true,
        link,
        stars = product.Stars,
        title = product.Title,
        playerId,
        gift = isGift,
        giftName,
    });
});

// Подтверждение Telegram ↔ игровой ID (код в личку в игре).
app.MapPost("/api/verify/start", async (HttpContext ctx) =>
{
    var me = await Auth(ctx);
    if (!me.Verified || me.TelegramId <= 0)
    {
        ctx.Response.StatusCode = 401;
        await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "Открой магазин из @StandReworkBot" });
        return;
    }
    string id = Field(await BodyOf(ctx), "id").Trim();
    if (string.IsNullOrEmpty(id))
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "Введи игровой ID" });
        return;
    }
    await Relay(ctx, await game.PostAsync("/api/admin/telegram-verify-start",
        new { telegramId = me.TelegramId.ToString(), id }, ctx.RequestAborted));
});

app.MapPost("/api/verify/confirm", async (HttpContext ctx) =>
{
    var me = await Auth(ctx);
    if (!me.Verified || me.TelegramId <= 0)
    {
        ctx.Response.StatusCode = 401;
        await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "Открой магазин из @StandReworkBot" });
        return;
    }
    string code = Field(await BodyOf(ctx), "code").Trim();
    if (string.IsNullOrEmpty(code))
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "Введи код из игры" });
        return;
    }
    var r = await game.PostAsync("/api/admin/telegram-verify-confirm",
        new { telegramId = me.TelegramId.ToString(), code }, ctx.RequestAborted);
    if (r.status == 200)
    {
        try
        {
            using var doc = JsonDocument.Parse(r.json);
            if (doc.RootElement.TryGetProperty("playerId", out var pidEl))
            {
                string pid = pidEl.GetString() ?? "";
                if (!string.IsNullOrEmpty(pid))
                    LinkStore.Upsert(me.TelegramId, pid);
            }
        }
        catch { /* ответ RPC уже ок — связку дублируем best-effort */ }
    }
    await Relay(ctx, r);
});

// Промокод на свой аккаунт — тем же методом, что и в игре.
app.MapPost("/api/promo", async (HttpContext ctx) =>
{
    var body = await BodyOf(ctx);
    var me = await Auth(ctx);

    // Промокод можно активировать прямо на сайте: ID в профиле, в заголовке
    // или одним запросом вместе с кодом — заходить в клиент не нужно.
    string playerId = me.PlayerId;
    if (string.IsNullOrEmpty(playerId))
        playerId = Field(body, "playerId").Trim();
    if (string.IsNullOrEmpty(playerId))
        playerId = Field(body, "id").Trim();

    if (string.IsNullOrEmpty(playerId))
    {
        ctx.Response.StatusCode = 409;
        await ctx.Response.WriteAsJsonAsync(new
        {
            ok = false,
            code = "not_linked",
            error = "Укажи игровой ID — награда начислится на этот аккаунт."
        });
        return;
    }

    string code = Field(body, "code").Trim();
    if (string.IsNullOrEmpty(code))
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "Введи промокод" });
        return;
    }

    Console.WriteLine($"[Web] промокод player={playerId} код={code}");
    await Relay(ctx, await game.PostAsync("/api/admin/promo",
        new { id = playerId, code }, ctx.RequestAborted));
});

// ---------- Вход в админку ----------

// Бот просит одноразовую ссылку для своего админа. Ключ — общий секрет,
// снаружи сюда не попасть: сайт слушает только localhost.
app.MapPost("/api/bot/admin-link", async (HttpContext ctx) =>
{
    var body = await BodyOf(ctx);
    string key = Field(body, "key");
    if (string.IsNullOrEmpty(botSharedKey) || key != botSharedKey)
    {
        ctx.Response.StatusCode = 403;
        await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "нет доступа" });
        return;
    }

    if (!long.TryParse(Field(body, "telegramId"), out long tgId) || !AdminStore.IsAdmin(tgId))
    {
        ctx.Response.StatusCode = 403;
        await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "не админ" });
        return;
    }

    string ticket = AdminSessions.IssueTicket(tgId);
    await ctx.Response.WriteAsJsonAsync(new
    {
        ok = true,
        url = cfg.PublicUrl.TrimEnd('/') + "/?admin=" + ticket,
        minutes = 15,
    });
});

// Обмен одноразового билета на сессию админки.
app.MapPost("/api/admin-session", async (HttpContext ctx) =>
{
    string ticket = Field(await BodyOf(ctx), "ticket");
    if (!AdminSessions.Redeem(ticket, out string token, out long tgId))
    {
        ctx.Response.StatusCode = 401;
        await ctx.Response.WriteAsJsonAsync(new
        {
            ok = false,
            error = "Ссылка уже использована или устарела. Нажми кнопку в боте ещё раз."
        });
        return;
    }
    Console.WriteLine($"[Web] админ-сессия выдана tg={tgId}");
    await ctx.Response.WriteAsJsonAsync(new { ok = true, token, telegramId = tgId, hours = 12 });
});

// Админка: всё, что умеют инлайн-кнопки бота.
app.MapMethods("/api/admin/{**route}", new[] { "GET", "POST" }, async (HttpContext ctx) =>
{
    var me = await Auth(ctx);
    if (!me.IsAdmin)
    {
        ctx.Response.StatusCode = 403;
        await ctx.Response.WriteAsJsonAsync(new
        {
            ok = false,
            code = "not_admin",
            error = "Нет доступа. Открой бота и нажми «Админка на сайте»."
        });
        return;
    }

    string route = ctx.Request.Path.Value ?? "";
    var body = await BodyOf(ctx);

    if (ctx.Request.Method == "GET")
    {
        var q = new Dictionary<string, string>();
        foreach (var kv in ctx.Request.Query)
            if (kv.Key != "key") q[kv.Key] = kv.Value.ToString();
        await Relay(ctx, await game.GetAsync(route, q, ctx.RequestAborted));
        return;
    }

    Console.WriteLine($"[Web] админка tg={me.TelegramId} {route}");
    await Relay(ctx, await game.PostAsync(route, new
    {
        id = Field(body, "id"),
        kind = Field(body, "kind"),
        amount = Field(body, "amount"),
        itemId = Field(body, "itemId"),
        action = Field(body, "action"),
        reason = Field(body, "reason"),
        name = Field(body, "name"),
        value = Field(body, "value"),
        newId = Field(body, "newId"),
        code = Field(body, "code"),
        uses = Field(body, "uses"),
        gold = Field(body, "gold"),
        spins = Field(body, "spins"),
        goldpass = Field(body, "goldpass"),
        adminTg = me.TelegramId.ToString(),
    }, ctx.RequestAborted));
});

// SPA: всё, что не API и не файл, отдаём как index.html.
app.MapFallback(async ctx =>
{
    if (ctx.Request.Path.StartsWithSegments("/api"))
    {
        ctx.Response.StatusCode = 404;
        await ctx.Response.WriteAsJsonAsync(new { ok = false, error = "нет такого метода" });
        return;
    }
    string index = Path.Combine(cfg.SiteRoot, "index.html");
    if (File.Exists(index))
    {
        ctx.Response.ContentType = "text/html; charset=utf-8";
        await ctx.Response.SendFileAsync(index);
        return;
    }
    ctx.Response.StatusCode = 503;
    await ctx.Response.WriteAsync("Сайт ещё не собран: нет " + index);
});

Console.WriteLine($"[Web] слушаю http://{cfg.BindAddress}:{cfg.Port}");
Console.WriteLine($"[Web] сайт: {cfg.SiteRoot}");
Console.WriteLine($"[Web] токен бота: {(bot.HasToken ? "есть" : "НЕ НАЙДЕН — покупки работать не будут")}");
Console.WriteLine($"[Web] связок аккаунтов: {LinkStore.Count}");

// Регистрируем кнопку Mini App в боте — так магазин открывается внутри Telegram
// и подпись приходит сама. Обычной ссылкой сайт тоже работает, по введённому ID.
_ = Task.Run(async () =>
{
    var (ok, err) = await bot.EnsureMenuButtonAsync(cfg.PublicUrl, CancellationToken.None);
    Console.WriteLine(ok
        ? $"[Web] кнопка Mini App в боте настроена на {cfg.PublicUrl}"
        : $"[Web] кнопку Mini App поставить не вышло: {err}");
});

app.Run();

/// <summary>Кто выполняет запрос. Verified — личность подтверждена Telegram, а не введена руками.</summary>
public sealed record Caller(
    long TelegramId, string Username, string FirstName,
    bool Verified, string PlayerId, bool IsAdmin);
