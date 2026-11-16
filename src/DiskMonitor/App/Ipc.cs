using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using DiskMonitor.Diagnostics;

namespace DiskMonitor.App;

/// <summary>
/// One-line messages from a second launch (or a toast click) to the instance already running.
/// A named pipe works whether or not that instance has a window, and across elevation levels.
/// </summary>
internal static class Ipc
{
    private const int MaxMessageLength = 4096;

    private static string PipeName => $"DiskMonitor.Ipc.{Elevation.CurrentUserSid}.{Process.GetCurrentProcess().SessionId}";

    /// <summary>Sends a message to the running instance. Returns false if none answered in time.</summary>
    public static bool TrySend(string message, int timeoutMilliseconds = 4000)
    {
        try
        {
            using NamedPipeClientStream client = new(".", PipeName, PipeDirection.Out);
            client.Connect(timeoutMilliseconds);
            byte[] bytes = Encoding.UTF8.GetBytes(message.ReplaceLineEndings(" ") + "\n");
            client.Write(bytes, 0, bytes.Length);
            client.Flush();
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Listens until disposed, passing each received line to <paramref name="onMessage"/> on a pool thread.</summary>
    public static IDisposable Listen(Action<string> onMessage) => new Server(onMessage);

    /// <summary>Access for the signed-in user at any elevation level, administrators, and the system.</summary>
    public static PipeSecurity CreateUserPipeSecurity()
    {
        PipeSecurity security = new();
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        if (identity.User is { } user)
        {
            security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        }

        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        return security;
    }

    private sealed class Server : IDisposable
    {
        private readonly Action<string> _onMessage;
        private readonly CancellationTokenSource _stop = new();

        public Server(Action<string> onMessage)
        {
            _onMessage = onMessage;
            _ = Task.Run(RunAsync);
        }

        public void Dispose() => _stop.Cancel();

        private async Task RunAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    await using NamedPipeServerStream server = NamedPipeServerStreamAcl.Create(
                        PipeName,
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous,
                        MaxMessageLength,
                        0,
                        CreateUserPipeSecurity());
                    await server.WaitForConnectionAsync(_stop.Token);

                    using CancellationTokenSource readLimit = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                    readLimit.CancelAfter(TimeSpan.FromSeconds(5));
                    byte[] buffer = new byte[MaxMessageLength];
                    int length = 0;
                    while (length < buffer.Length)
                    {
                        int read = await server.ReadAsync(buffer.AsMemory(length), readLimit.Token);
                        if (read == 0)
                        {
                            break;
                        }

                        length += read;
                        if (Array.IndexOf(buffer, (byte)'\n', 0, length) >= 0)
                        {
                            break;
                        }
                    }

                    string message = Encoding.UTF8.GetString(buffer, 0, length).Trim();
                    if (message.Length > 0)
                    {
                        _onMessage(message);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Shutting down, or a client that connected and never wrote.
                }
                catch (Exception ex)
                {
                    ErrorLog.Write("IPC", ex);
                    try
                    {
                        await Task.Delay(1000, _stop.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        }
    }
}
