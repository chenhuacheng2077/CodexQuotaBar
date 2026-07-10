using System.Diagnostics;
using System.Text.Json;

var codexPath = args.FirstOrDefault() ?? Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "OpenAI", "Codex", "bin", "codex.exe");

if (!File.Exists(codexPath))
{
    Console.Error.WriteLine($"codex executable not found: {codexPath}");
    return 2;
}

using var process = Process.Start(new ProcessStartInfo(codexPath, "app-server")
{
    RedirectStandardInput = true,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    UseShellExecute = false,
    CreateNoWindow = true
}) ?? throw new InvalidOperationException("Failed to start codex app-server.");

var messages = new Dictionary<long, JsonElement>();
var nextId = 1L;

async Task<JsonElement> RequestAsync(string method, object? parameters = null)
{
    var id = nextId++;
    var payload = JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters ?? new { } });
    await process.StandardInput.WriteLineAsync(payload);
    await process.StandardInput.FlushAsync();

    while (true)
    {
        var line = await process.StandardOutput.ReadLineAsync();
        if (line is null)
        {
            throw new InvalidOperationException($"app-server stopped: {await process.StandardError.ReadToEndAsync()}");
        }

        using var document = JsonDocument.Parse(line);
        var root = document.RootElement.Clone();
        Console.WriteLine(Sanitize(root).GetRawText());
        if (root.TryGetProperty("id", out var responseId) && responseId.TryGetInt64(out var receivedId) && receivedId == id)
        {
            return root;
        }
    }
}

JsonElement Sanitize(JsonElement value)
{
    if (value.ValueKind == JsonValueKind.Object)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value.EnumerateObject().ToDictionary(
            property => property.Name,
            property => (property.Name.Contains("token", StringComparison.OrdinalIgnoreCase) ||
                         property.Name.Contains("authorization", StringComparison.OrdinalIgnoreCase) ||
                         property.Name.Contains("email", StringComparison.OrdinalIgnoreCase))
                ? (object)"[redacted]"
                : Sanitize(property.Value))));
        return doc.RootElement.Clone();
    }

    if (value.ValueKind == JsonValueKind.Array)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value.EnumerateArray().Select(Sanitize)));
        return doc.RootElement.Clone();
    }

    return value.Clone();
}

await RequestAsync("initialize", new { clientInfo = new { name = "codex-quota-probe", version = "0.1.0" }, capabilities = new { } });
await process.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"initialized\",\"params\":{}}");
await process.StandardInput.FlushAsync();
await RequestAsync("account/read", new { });
await RequestAsync("account/rateLimits/read", new { });

Console.WriteLine("Probe complete. Waiting 10 seconds for account/rateLimits/updated notifications.");
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
try
{
    while (!cancellation.IsCancellationRequested)
    {
        var line = await process.StandardOutput.ReadLineAsync(cancellation.Token);
        if (line is not null)
        {
            using var document = JsonDocument.Parse(line);
            Console.WriteLine(Sanitize(document.RootElement).GetRawText());
        }
    }
}
catch (OperationCanceledException)
{
}

return 0;
