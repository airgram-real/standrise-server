using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace StandRiseServer.RpcServer
{
    public class AmiraAiService
    {
        private const string ApiUrl = "https://router.cheap/v1/chat/completions";
        private const string ApiKey = "sk-lgVeejiXN7zcwiGT0CHg3dUvcMMqryxxyLjASIxSYz4uiDiG";
        private const string ModelName = "gemini-3.5-flash";

        private static readonly HttpClient _httpClient = new HttpClient();
        
        // Chat history storage (ChatId -> List of Messages)
        private readonly ConcurrentDictionary<long, List<ChatMessage>> _history = new();

        public class ChatMessage
        {
            [JsonPropertyName("role")]
            public string Role { get; set; }

            [JsonPropertyName("content")]
            public string Content { get; set; }
            
            [JsonPropertyName("name")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string Name { get; set; }
            
            [JsonPropertyName("tool_calls")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public List<ToolCall> ToolCalls { get; set; }
            
            [JsonPropertyName("tool_call_id")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string ToolCallId { get; set; }
        }

        public class ToolCall
        {
            [JsonPropertyName("id")]
            public string Id { get; set; }
            
            [JsonPropertyName("type")]
            public string Type { get; set; } = "function";
            
            [JsonPropertyName("function")]
            public ToolFunction Function { get; set; }
        }

        public class ToolFunction
        {
            [JsonPropertyName("name")]
            public string Name { get; set; }
            
            [JsonPropertyName("arguments")]
            public string Arguments { get; set; }
        }

        public class AiResponse
        {
            [JsonPropertyName("choices")]
            public List<Choice> Choices { get; set; }
        }

        public class Choice
        {
            [JsonPropertyName("message")]
            public ChatMessage Message { get; set; }
        }

        private readonly string _systemPrompt = 
            "Ты — Амира, вежливая и дружелюбная помощница администраторов сервера в Telegram-чате. " +
            "Твоя задача — помогать админам управлять сервером адекватно и профессионально.\n" +
            "У тебя есть доступ к функциям (tools), чтобы создавать промокоды (create_promo) и банить нарушителей (ban_user). " +
            "Отвечай от женского лица.\n" +
            "ПРАВИЛА:\n" +
            "1. Общайся адекватно, вежливо и по делу.\n" +
            "2. Если просят выдать промокод или забанить - ТЫ ОБЯЗАНА вызвать соответствующую функцию (tool call). Не выдумывай промокоды сама, используй функцию!\n" +
            "3. После успешного вызова функции, обязательно сообщи пользователю результат (сгенерированный код или статус бана), который вернет тебе функция.";

        private object GetTools()
        {
            return new object[]
            {
                new
                {
                    type = "function",
                    function = new
                    {
                        name = "create_promo",
                        description = "Создает промокод на указанный предмет.",
                        parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                item_name = new { type = "string", description = "Название предмета или ID, например 'karambit gold', 'm9 bayonet', '125'" },
                                gold = new { type = "integer", description = "Количество голды в промокоде" },
                                silver = new { type = "integer", description = "Количество серебра в промокоде" },
                                uses = new { type = "integer", description = "Количество активаций" }
                            },
                            required = new[] { "item_name", "gold", "silver", "uses" }
                        }
                    }
                },
                new
                {
                    type = "function",
                    function = new
                    {
                        name = "ban_user",
                        description = "Банит пользователя.",
                        parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                player_id = new { type = "string", description = "ID игрока (24-символьный hex)" },
                                reason = new { type = "string", description = "Причина бана (Toxic, Scam, Cheats)" }
                            },
                            required = new[] { "player_id", "reason" }
                        }
                    }
                }
            };
        }

        public async Task<ChatMessage> SendMessageAsync(long chatId, string userName, string userText, ChatMessage toolResult = null)
        {
            if (!_history.TryGetValue(chatId, out var history))
            {
                history = new List<ChatMessage>();
                _history[chatId] = history;
            }

            if (toolResult != null)
            {
                history.Add(toolResult);
            }
            else if (!string.IsNullOrEmpty(userText))
            {
                history.Add(new ChatMessage { Role = "user", Name = userName, Content = userText });
            }

            // Ограничение истории (последние 10 сообщений)
            if (history.Count > 10)
            {
                history.RemoveRange(0, history.Count - 10);
            }

            var messagesPayload = new List<ChatMessage>
            {
                new ChatMessage { Role = "system", Content = _systemPrompt }
            };
            messagesPayload.AddRange(history);

            var requestBody = new
            {
                model = ModelName,
                messages = messagesPayload,
                tools = GetTools(),
                tool_choice = "auto",
                temperature = 0.5,
                max_tokens = 250
            };

            string jsonBody = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
            {
                Content = content
            };
            request.Headers.Add("Authorization", $"Bearer {ApiKey}");

            try
            {
                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                string responseJson = await response.Content.ReadAsStringAsync();
                var aiResponse = JsonSerializer.Deserialize<AiResponse>(responseJson);
                var aiMessage = aiResponse?.Choices?.FirstOrDefault()?.Message;

                if (aiMessage != null)
                {
                    history.Add(aiMessage);
                    return aiMessage;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AmiraAiService] Error: {ex.Message}");
            }

            return new ChatMessage { Role = "assistant", Content = "Что-то пошло не так, мои нейроны запутались... 😅" };
        }
        
        public void ClearHistory(long chatId)
        {
            _history.TryRemove(chatId, out _);
        }
    }
}
