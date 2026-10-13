using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace CodexMonitor;

/// <summary>Only initialize and account/rateLimits/read are sent. Never starts a model turn.</summary>
public sealed class AppServerClient : IDisposable
{
    private readonly Process process;
    private readonly ProcessJob job;
    private readonly CancellationTokenSource lifetime = new();
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> pending = new();
    private readonly SemaphoreSlim writer = new(1, 1);
    private readonly Task reader;
    private readonly Task errors;
    private long nextId;
    private int disposed;
    public int ProcessId => process.Id;

    private AppServerClient(string executable)
    {
        job = new ProcessJob();
        process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };
        process.StartInfo.ArgumentList.Add("app-server");
        process.StartInfo.ArgumentList.Add("--listen");
        process.StartInfo.ArgumentList.Add("stdio://");
        try
        {
            process.Start();
            job.Assign(process); // Closing the job also kills descendants if the UI crashes.
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
            process.Dispose(); job.Dispose(); lifetime.Dispose(); writer.Dispose();
            throw;
        }
        reader = ReadMessagesAsync();
        errors = DrainErrorsAsync();
    }

    public static async Task<AppServerClient> ConnectAsync(string executable, CancellationToken cancellation)
    {
        var client = new AppServerClient(executable);
        try
        {
            await client.RequestAsync("initialize", new
            {
                clientInfo = new { name = "codex_monitor", title = "CodexMonitor", version = "1.0.0" }
            }, cancellation).ConfigureAwait(false);
            await client.WriteAsync(new { method = "initialized", @params = new { } }, cancellation).ConfigureAwait(false);
            return client;
        }
        catch { client.Dispose(); throw; }
    }

    public Task<JsonElement> ReadQuotaAsync(CancellationToken cancellation) => RequestAsync("account/rateLimits/read", new { }, cancellation);

    private async Task<JsonElement> RequestAsync(string method, object parameters, CancellationToken cancellation)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (reader.IsCompleted) throw new IOException("App-server connection closed.");
        var id = Interlocked.Increment(ref nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = completion;
        try
        {
            await WriteAsync(new { id, method, @params = parameters }, cancellation).ConfigureAwait(false);
            return await completion.Task.WaitAsync(cancellation).ConfigureAwait(false);
        }
        finally { pending.TryRemove(id, out _); }
    }

    private async Task WriteAsync(object message, CancellationToken cancellation)
    {
        await writer.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), cancellation).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(cancellation).ConfigureAwait(false);
        }
        finally { writer.Release(); }
    }

    private async Task ReadMessagesAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var line = await process.StandardOutput.ReadLineAsync(lifetime.Token).ConfigureAwait(false);
                if (line is null) break;
                if (line.Length > 1_048_576) throw new JsonException("Oversized app-server response.");
                using var doc = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 64 });
                var message = doc.RootElement;
                if (message.ValueKind != JsonValueKind.Object) throw new JsonException("Invalid app-server envelope.");
                if (!message.TryGetProperty("id", out var id)) continue; // Notifications cannot refresh our read timestamp.
                if (message.TryGetProperty("method", out _))
                {
                    // Do not accept credential callbacks or perform unexpected server-initiated operations.
                    await WriteAsync(new { id = id.Clone(), error = new { code = -32601, message = "Client method not supported" } }, lifetime.Token).ConfigureAwait(false);
                    continue;
                }
                if (id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out var number) || !pending.TryGetValue(number, out var completion)) continue;
                if (message.TryGetProperty("error", out var error))
                {
                    var code = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("code", out var codeElement) ? codeElement.ToString() : "unknown";
                    // Never expose upstream error text: it can include request URLs or authentication details.
                    completion.TrySetException(new AppServerException($"Codex refuse la lecture (code {code}). Vérifiez « codex login status » et la connexion réseau."));
                }
                else if (message.TryGetProperty("result", out var result)) completion.TrySetResult(result.Clone());
                else completion.TrySetException(new JsonException("Missing app-server result."));
            }
            FailPending(new IOException("App-server connection closed."));
        }
        catch (Exception e) when (e is IOException or JsonException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        { FailPending(e); }
    }

    private async Task DrainErrorsAsync()
    {
        // Drain without accumulating or persisting Codex diagnostics, which may contain private information.
        var buffer = new char[2048];
        try { while (await process.StandardError.ReadAsync(buffer.AsMemory(), lifetime.Token).ConfigureAwait(false) > 0) { } }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException or InvalidOperationException) { }
    }

    private void FailPending(Exception error)
    {
        foreach (var request in pending.Values) request.TrySetException(error);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel();
        FailPending(new ObjectDisposedException(nameof(AppServerClient)));
        job.Dispose();
        try { if (!process.HasExited) { process.Kill(true); process.WaitForExit(2000); } } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        // Disposal of stream owners only after their read loops have stopped.
        try { Task.WaitAll([reader, errors], 2000); } catch (AggregateException) { }
        process.Dispose();
        lifetime.Dispose();
        // SemaphoreSlim owns no native handle unless AvailableWaitHandle is accessed.
    }
}

public sealed class AppServerException(string message) : Exception(message);

public static class CodexLocation
{
    public static string Find()
    {
        var overridePath = Environment.GetEnvironmentVariable("CODEX_MONITOR_CODEX_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            if (Path.IsPathFullyQualified(overridePath) && File.Exists(overridePath) && overridePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return overridePath;
            throw new FileNotFoundException("CODEX_MONITOR_CODEX_PATH doit désigner un exécutable existant par son chemin absolu.");
        }
        var native = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "OpenAI", "Codex", "bin", "codex.exe");
        if (File.Exists(native)) return native;
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (!Path.IsPathFullyQualified(folder)) continue;
            try { var candidate = Path.Combine(folder, "codex.exe"); if (File.Exists(candidate)) return candidate; }
            catch (ArgumentException) { }
        }
        throw new FileNotFoundException("Codex CLI natif introuvable. Installez Codex et connectez-vous avec « codex login ».");
    }
}
