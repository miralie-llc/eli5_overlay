using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace F1.Infrastructure;

/// <summary>LF-framed JSON RPC. Event lifetime is exactly the subprocess lifetime.</summary>
public sealed class RpcConnection : IAsyncDisposable
{
    private readonly Process process;
    private readonly ProcessJob job;
    private readonly CancellationTokenSource lifetime = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> pending = new();
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private readonly Task reader;
    private readonly Task errors;
    public event Action<JsonElement>? EventReceived;
    public event Action? Failed;
    public RpcConnection(ProcessStartInfo info)
    {
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardInput = info.RedirectStandardOutput = info.RedirectStandardError = true;
        info.StandardInputEncoding = new UTF8Encoding(false);
        info.StandardOutputEncoding = Encoding.UTF8;
        process = new Process { StartInfo = info };
        if (!process.Start()) throw new IOException("Pi did not start.");
        try { job = new ProcessJob(process); }
        catch { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } process.Dispose(); throw; }
        reader = ReadAsync();
        errors = DrainErrorsAsync();
    }
    public async Task<JsonElement> CommandAsync(string type, object? data, CancellationToken token)
    {
        var id = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = completion;
        try
        {
            var fields = data is null ? new Dictionary<string, object?>() : JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(data))!;
            fields["id"] = id; fields["type"] = type;
            await SendAsync(fields, token);
            var response = await completion.Task.WaitAsync(token);
            if (response.TryGetProperty("success", out var success) && !success.GetBoolean())
                throw new InvalidOperationException("Pi rejected the operation. Check the selected resources and model.");
            return response;
        }
        finally { pending.TryRemove(id, out _); }
    }
    public async Task SendAsync(object data, CancellationToken token)
    {
        await writeLock.WaitAsync(token);
        try
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(data).AsMemory(), token);
            await process.StandardInput.FlushAsync(token);
        }
        finally { writeLock.Release(); }
    }
    private async Task ReadAsync()
    {
        try
        {
            await foreach (var line in ReadRecordsAsync(process.StandardOutput, lifetime.Token))
            {
                using var document = JsonDocument.Parse(line);
                var value = document.RootElement.Clone();
                if (value.TryGetProperty("type", out var type) && type.GetString() == "response"
                    && value.TryGetProperty("id", out var id) && pending.TryGetValue(id.GetString()!, out var completion))
                    completion.TrySetResult(value);
                else EventReceived?.Invoke(value);
            }
            if (!lifetime.IsCancellationRequested) throw new IOException("Pi disconnected.");
        }
        catch (Exception) when (!lifetime.IsCancellationRequested)
        {
            foreach (var item in pending.Values) item.TrySetException(new IOException("Pi disconnected or returned invalid RPC data."));
            Failed?.Invoke();
        }
        catch (OperationCanceledException) { }
    }
    private async Task DrainErrorsAsync()
    {
        // Never retain stderr: third-party extensions may print credentials or content.
        var buffer = new char[4096];
        try { while (await process.StandardError.ReadAsync(buffer, lifetime.Token) != 0) { } }
        catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
    }
    public static async IAsyncEnumerable<string> ReadRecordsAsync(TextReader reader,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        var buffer = new char[4096];
        var record = new StringBuilder();
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) != 0)
        {
            for (var i = 0; i < count; i++)
            {
                if (buffer[i] == '\n')
                {
                    if (record.Length > 0 && record[^1] == '\r') record.Length--;
                    if (record.Length > 0) yield return record.ToString();
                    record.Clear();
                }
                else record.Append(buffer[i]);
                if (record.Length > 4 * 1024 * 1024) throw new InvalidDataException("RPC record exceeds limit.");
            }
        }
        if (record.Length != 0) throw new InvalidDataException("Incomplete RPC record.");
    }
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        job.Dispose();
        foreach (var item in pending.Values) item.TrySetCanceled();
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        try { await Task.WhenAll(reader, errors).WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or IOException) { }
        process.Dispose();
        lifetime.Dispose();
    }
}
