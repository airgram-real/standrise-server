using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProjectRework.Web;

/// <summary>
/// Клиент локального админ-API игрового сервера (HttpApiServer, порт 2224).
/// Сайт сам ничего игрокам не выдаёт: он только пересылает запрос процессу игры,
/// где выдача идёт тем же кодом, что и у инлайн-кнопок бота.
/// </summary>
public sealed class GameApi
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly string _baseUrl;
    private readonly string _key;

    public GameApi(string baseUrl, string botToken)
    {
        _baseUrl = (baseUrl ?? "http://127.0.0.1:2224").TrimEnd('/');
        _key = string.IsNullOrEmpty(botToken)
            ? ""
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(botToken))).ToLowerInvariant();
    }

    public bool HasKey => !string.IsNullOrEmpty(_key);

    /// <summary>GET /api/admin/... Возвращает (код, json).</summary>
    public async Task<(int status, string json)> GetAsync(string route, IDictionary<string, string>? q, CancellationToken ct)
    {
        var parts = new List<string> { "key=" + Uri.EscapeDataString(_key) };
        if (q != null)
            foreach (var kv in q)
                if (!string.IsNullOrEmpty(kv.Value))
                    parts.Add(Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value));
        return await SendAsync(HttpMethod.Get, route + "?" + string.Join("&", parts), null, ct);
    }

    /// <summary>POST с готовым JSON-телом; key и query — в URL.</summary>
    public async Task<(int status, string json)> PostRawAsync(string route, string jsonBody, IDictionary<string, string>? q, CancellationToken ct)
    {
        var parts = new List<string> { "key=" + Uri.EscapeDataString(_key) };
        if (q != null)
            foreach (var kv in q)
                if (!string.IsNullOrEmpty(kv.Value))
                    parts.Add(Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value));
        var content = new StringContent(string.IsNullOrWhiteSpace(jsonBody) ? "{}" : jsonBody, Encoding.UTF8, "application/json");
        return await SendAsync(HttpMethod.Post, route + "?" + string.Join("&", parts), content, ct);
    }

    /// <summary>POST /api/admin/... Ключ подкладывается в тело сам.</summary>
    public async Task<(int status, string json)> PostAsync(string route, object body, CancellationToken ct)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var p in body.GetType().GetProperties())
            dict[p.Name] = p.GetValue(body);
        dict["key"] = _key;
        var content = new StringContent(JsonSerializer.Serialize(dict), Encoding.UTF8, "application/json");
        return await SendAsync(HttpMethod.Post, route, content, ct);
    }

    private async Task<(int, string)> SendAsync(HttpMethod method, string route, HttpContent? content, CancellationToken ct)
    {
        if (!HasKey)
            return (503, "{\"ok\":false,\"error\":\"нет токена бота — ключ к игровому серверу не собран\"}");
        try
        {
            using var req = new HttpRequestMessage(method, _baseUrl + route);
            if (content != null) req.Content = content;
            using var resp = await _http.SendAsync(req, ct);
            string text = await resp.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(text))
                text = "{\"ok\":false,\"error\":\"игровой сервер ответил пусто\"}";
            return ((int)resp.StatusCode, text);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Web] игровой сервер недоступен ({route}): {ex.Message}");
            return (502, JsonSerializer.Serialize(new { ok = false, error = "игровой сервер не отвечает: " + ex.Message }));
        }
    }
}
