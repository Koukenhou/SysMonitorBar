using System.IO;
using System.IO.Pipes;
using System.Security.Principal;

namespace SysMonitorBar.Infrastructure;

/// <summary>
/// One instance per user session (named mutex). A second launch signals the running
/// instance over a named pipe so that it shows the bar, then exits.
/// </summary>
internal sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Local\SysMonitorBar.SingleInstance";

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _cts = new();

    private SingleInstanceGuard(Mutex mutex)
    {
        _mutex = mutex;
    }

    private static string PipeName { get; } =
        "SysMonitorBar.Activate." + (WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName);

    /// <summary>Returns null when another instance already owns the mutex.</summary>
    public static SingleInstanceGuard? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }
        return new SingleInstanceGuard(mutex);
    }

    /// <summary>Asks the running instance to show its bar. Best effort.</summary>
    public static void SignalExistingInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(1000);
            client.WriteByte(1);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Listens for activation requests; <paramref name="onActivate"/> runs on a background thread.</summary>
    public void StartListening(Action onActivate)
    {
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                    var buffer = new byte[1];
                    if (await server.ReadAsync(buffer, ct).ConfigureAwait(false) > 0)
                    {
                        onActivate();
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (IOException)
                {
                    // Broken connection from a client; keep listening.
                }
            }
        }, ct);
    }

    /// <summary>Must be called on the thread that acquired the mutex (the UI thread).</summary>
    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
