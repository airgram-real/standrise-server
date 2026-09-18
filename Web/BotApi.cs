using System.Text;
using System.Text.Json;

namespace ProjectRework.Web;

/// <summary>
/// Тонкий клиент Bot API. Нужен ровно для одного вызова — createInvoiceLink.
/// Приём платежа, pre_checkout и выдачу делает уже работающий бот, сюда это
/// не переносится.
/// </summary>
public sealed class BotApi
{
    private readonly HttpClient _http;
    private readonly string _token;

    public BotApi(string token)
    {
        _token = token ?? "";
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    }

    public bool HasToken => !string.IsNullOrWhiteSpace(_token);

    /// <summary>
    /// Ссылка на счёт в звёздах. Для XTR provider_token обязан быть пустым,
    /// а сумма в prices указывается прямо в звёздах, без умножения на 100.
    /// </summary>
    public async Task<(bool Ok, string Link, string Error)> CreateInvoiceLinkAsync(
        string title, string description, string payload, int stars, CancellationToken ct)
    {
        if (!HasToken) return (false, "", "на сервере нет токена бота");
        if (stars <= 0) return (false, "", "цена должна быть больше нуля");

        var body = new Dictionary<string, object>
        {
            ["title"] = Trim(title, 32),
            ["description"] = Trim(description, 255),
            ["payload"] = payload,
            ["provider_token"] = "",
            ["currency"] = "XTR",
            ["prices"] = new[] { new Dictionary<string, object> { ["label"] = "Цена", ["amount"] = stars } },
        };

        try
        {
            using var content = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync(
                $"https://api.telegram.org/bot{_token}/createInvoiceLink", content, ct);
            string json = await resp.Content.ReadAsStringAsync(ct);

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean())
                return (true, doc.RootElement.GetProperty("result").GetString() ?? "", "");

            string desc = doc.RootElement.TryGetProperty("description", out var d)
                ? (d.GetString() ?? "") : json;
            return (false, "", desc);
        }
        catch (Exception ex)
        {
            return (false, "", ex.Message);
        }
    }

    /// <summary>
    /// Ставит в боте кнопку меню, открывающую сайт как Mini App.
    ///
    /// Без неё магазин открывается обычной страницей, Telegram не передаёт
    /// initData, и сервер честно отвечает «проверка не прошла» — проверять нечего.
    /// Вызывается при каждом старте сайта, операция идемпотентная.
    /// </summary>
    public async Task<(bool Ok, string Error)> EnsureMenuButtonAsync(string publicUrl, CancellationToken ct)
    {
        if (!HasToken) return (false, "нет токена бота");
        if (string.IsNullOrWhiteSpace(publicUrl) || !publicUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return (false, "Telegram принимает только https-адрес Mini App, сейчас: " + publicUrl);

        var body = new Dictionary<string, object>
        {
            ["menu_button"] = new Dictionary<string, object>
            {
                ["type"] = "web_app",
                ["text"] = "Магазин",
                ["web_app"] = new Dictionary<string, object> { ["url"] = publicUrl },
            },
        };

        try
        {
            using var content = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync(
                $"https://api.telegram.org/bot{_token}/setChatMenuButton", content, ct);
            string json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean())
                return (true, "");
            return (false, doc.RootElement.TryGetProperty("description", out var d)
                ? (d.GetString() ?? "") : json);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>Телеграм режет длинные поля — обрезаем сами, чтобы не ловить ошибку.</summary>
    private static string Trim(string s, int max)
    {
        s ??= "";
        return s.Length <= max ? s : s.Substring(0, max);
    }
}
