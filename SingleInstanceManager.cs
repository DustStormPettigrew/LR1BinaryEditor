using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LR1BinaryEditor
{
    internal sealed class SingleInstanceManager : IDisposable
    {
        private const string k_mutexName = "Local\\LR1BinaryEditor.SingleInstance";
        private const string k_pipeName = "LR1BinaryEditor.SingleInstance.Args";
        private static readonly Encoding ms_utf8NoBom = new UTF8Encoding(false);

        private readonly Mutex m_mutex;
        private readonly CancellationTokenSource m_cancellation = new CancellationTokenSource();
        private Action<string[]> m_receivedArgs;
        private Task m_serverTask;

        private SingleInstanceManager(Mutex mutex, bool isPrimaryInstance)
        {
            m_mutex = mutex;
            IsPrimaryInstance = isPrimaryInstance;
        }

        public bool IsPrimaryInstance { get; }

        public static SingleInstanceManager Create(string[] args)
        {
            Mutex mutex = new Mutex(true, k_mutexName, out bool createdNew);
            if (!createdNew)
            {
                SendArgsToPrimaryInstance(args);
                mutex.Dispose();
                return new SingleInstanceManager(null, false);
            }

            return new SingleInstanceManager(mutex, true);
        }

        public void StartServer(Action<string[]> receivedArgs)
        {
            if (!IsPrimaryInstance || m_serverTask != null)
                return;

            m_receivedArgs = receivedArgs;
            m_serverTask = Task.Run(RunServerAsync);
        }

        private async Task RunServerAsync()
        {
            while (!m_cancellation.IsCancellationRequested)
            {
                try
                {
                    using (NamedPipeServerStream pipe = new NamedPipeServerStream(
                        k_pipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous))
                    {
                        await pipe.WaitForConnectionAsync(m_cancellation.Token);
                        using (StreamReader reader = new StreamReader(pipe, ms_utf8NoBom))
                        {
                            string payload = await reader.ReadToEndAsync();
                            string[] args = JsonSerializer.Deserialize<string[]>(payload) ?? Array.Empty<string>();
                            m_receivedArgs?.Invoke(args);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch
                {
                    // Keep accepting future launches even if one handoff is malformed.
                }
            }
        }

        private static void SendArgsToPrimaryInstance(string[] args)
        {
            try
            {
                using (NamedPipeClientStream pipe = new NamedPipeClientStream(
                    ".",
                    k_pipeName,
                    PipeDirection.Out,
                    PipeOptions.Asynchronous))
                {
                    ConnectWithRetry(pipe);
                    using (StreamWriter writer = new StreamWriter(pipe, ms_utf8NoBom))
                    {
                        writer.Write(JsonSerializer.Serialize(args ?? Array.Empty<string>()));
                    }
                }
            }
            catch
            {
                // If handoff fails, still avoid starting a competing editor process.
            }
        }

        private static void ConnectWithRetry(NamedPipeClientStream pipe)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(4);
            while (true)
            {
                try
                {
                    pipe.Connect(250);
                    return;
                }
                catch (TimeoutException)
                {
                    if (DateTime.UtcNow >= deadline)
                        throw;
                }
            }
        }

        public void Dispose()
        {
            m_cancellation.Cancel();
            m_cancellation.Dispose();
            m_mutex?.ReleaseMutex();
            m_mutex?.Dispose();
        }
    }
}
