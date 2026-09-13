using System.Text.Json;

static class FakeRpc
{
    public static async Task RunAsync()
    {
        string? line;
        while ((line = await Console.In.ReadLineAsync()) is not null)
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var type = root.GetProperty("type").GetString();
            var id = root.TryGetProperty("id", out var identifier) ? identifier.GetString() : null;
            if (type == "hang") continue;
            if (type == "disconnect") return;
            if (type == "malformed") { Console.WriteLine("invalid-json"); continue; }
            if (type == "prompt")
            {
                Console.WriteLine(JsonSerializer.Serialize(new { type = "response", id, success = true }));
                Console.WriteLine("{\"type\":\"message_update\",\"assistantMessageEvent\":{\"type\":\"text_delta\",\"delta\":\"Hello\"}}");
                Console.WriteLine("{\"type\":\"agent_settled\"}");
                continue;
            }
            Console.WriteLine(JsonSerializer.Serialize(new { type = "response", id, success = true }));
        }
    }
}
