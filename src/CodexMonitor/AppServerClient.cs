using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CodexMonitor.Core;

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

    private AppServerClient(CodexCommand command)
    {
        job = new ProcessJob();
        process = new Process
        {
            StartInfo = new ProcessStartInfo(command.Executable)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };
        foreach (var argument in command.Arguments) process.StartInfo.ArgumentList.Add(argument);
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

    public static Task<AppServerClient> ConnectAsync(string executable, CancellationToken cancellation) =>
        ConnectAsync(CodexCommand.Native(executable), cancellation);

    public static async Task<AppServerClient> ConnectAsync(CodexCommand command, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var client = new AppServerClient(command);
        try
        {
            await client.RequestAsync("initialize", new
            {
                clientInfo = new { name = "codex_monitor", title = "CodexMonitor", version = AppPresentation.Version }
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
        if (reader.IsCompleted) throw new IOException(UiText.ConnectionClosed);
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
                if (line.Length > 1_048_576) throw new JsonException(UiText.OversizedResponse);
                using var doc = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 64 });
                var message = doc.RootElement;
                if (message.ValueKind != JsonValueKind.Object) throw new JsonException(UiText.InvalidEnvelope);
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
                    var code = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("code", out var codeElement) ? codeElement.ToString() : null;
                    // Never expose upstream error text: it can include request URLs or authentication details.
                    completion.TrySetException(AppServerException.FromCode(code));
                }
                else if (message.TryGetProperty("result", out var result)) completion.TrySetResult(result.Clone());
                else completion.TrySetException(new JsonException(UiText.MissingResult));
            }
            FailPending(new IOException(UiText.ConnectionClosed));
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
        // Give the Linux server EOF before closing its Windows WSL launcher.
        // A Windows Job Object alone cannot own Linux processes inside WSL.
        try { process.StandardInput.Close(); process.WaitForExit(2000); }
        catch (Exception e) when (e is IOException or InvalidOperationException) { }
        job.Dispose();
        try { if (!process.HasExited) { process.Kill(true); process.WaitForExit(2000); } } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        // Disposal of stream owners only after their read loops have stopped.
        try { Task.WaitAll([reader, errors], 2000); } catch (AggregateException) { }
        process.Dispose();
        lifetime.Dispose();
        // SemaphoreSlim owns no native handle unless AvailableWaitHandle is accessed.
    }
}

public sealed class AppServerException(string message) : Exception(message)
{
    public string? Code { get; private init; }
    private bool IsServerError { get; init; }
    public static AppServerException FromCode(string? code) => new("") { Code = code, IsServerError = true };
    public override string Message => IsServerError
        ? Localization.Format(nameof(UiText.ServerRefused), Code ?? UiText.UnknownBalance) : base.Message;
}
