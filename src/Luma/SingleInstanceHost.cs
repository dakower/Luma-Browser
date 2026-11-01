using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Luma;

public sealed class SingleInstanceHost : IDisposable
{
    private readonly string _name;
    private readonly Mutex? _mutex;
    private readonly CancellationTokenSource _cts = new();

    public bool IsPrimary { get; }
    public event Action<string[]>? CommandReceived;

    public SingleInstanceHost(string n)
    {
        _name = n;
        try
        {
            _mutex = new Mutex(true, n, out var createdNew);
            IsPrimary = createdNew;
        }
        catch (AbandonedMutexException)
        {
            // Previous process was terminated without releasing the mutex.
            // Operating system granted ownership to us, so we ARE the primary instance!
            IsPrimary = true;
        }
        catch
        {
            IsPrimary = false;
        }
    }

    public void StartListening()
    {
        if (!IsPrimary) return;
        _ = Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    using var s = new NamedPipeServerStream(_name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await s.WaitForConnectionAsync(_cts.Token);
                    using var r = new StreamReader(s);
                    var payload = await r.ReadToEndAsync(_cts.Token);
                    var args = JsonSerializer.Deserialize<string[]>(payload) ?? Array.Empty<string>();
                    CommandReceived?.Invoke(args);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    await Task.Delay(200, _cts.Token).ConfigureAwait(false);
                }
            }
        });
    }

    public void Send(string[] a)
    {
        try
        {
            using var c = new NamedPipeClientStream(".", _name, PipeDirection.Out);
            c.Connect(1500);
            using var w = new StreamWriter(c) { AutoFlush = true };
            w.Write(JsonSerializer.Serialize(a));
        }
        catch { }
    }

    public void Dispose()
    {
        _cts.Cancel();
        if (IsPrimary && _mutex is not null)
        {
            try { _mutex.ReleaseMutex(); } catch { }
        }
        _mutex?.Dispose();
        _cts.Dispose();
    }
}
