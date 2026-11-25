using System.Net.Sockets;
using System.Text.Json;
using UniChat.Common;
using Message = UniChat.Common.Message;

namespace UniChat.Client;

public class ChatClient
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private TcpClient? _tcp;

    public event Action<Message>? MessageReceived;
    public event Action<Message, byte[]>? FileReceived;
    public event Action? Disconnected;

    public async Task ConnectAsync(string host, int port, string username)
    {
        var client = new TcpClient();

        if (Interlocked.CompareExchange(ref _tcp, client, null) != null)
        {
            client.Dispose();
            throw new InvalidOperationException("Client đã có kết nối.");
        }

        try
        {
            await client.ConnectAsync(host, port);
            var stream = client.GetStream();

            await SendAsync(new Message
            {
                Type = "login",
                From = username
            });

            _ = Task.Run(() => ReceiveLoopAsync(client, stream));
        }
        catch
        {
            Interlocked.CompareExchange(ref _tcp, null, client);
            client.Dispose();
            throw;
        }
    }

    public async Task SendAsync(
        Message message,
        ReadOnlyMemory<byte> payload = default)
    {
        var client = Volatile.Read(ref _tcp)
            ?? throw new InvalidOperationException("Bạn chưa kết nối tới server.");

        await _sendLock.WaitAsync();

        try
        {
            if (!ReferenceEquals(client, Volatile.Read(ref _tcp)))
            {
                throw new InvalidOperationException("Kết nối đã thay đổi.");
            }

            await ChatFrame.WriteAsync(client.GetStream(), message, payload);
        }
        catch (Exception ex) when (ex is IOException or SocketException
                                   or ObjectDisposedException)
        {
            client.Dispose();
            throw;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReceiveLoopAsync(TcpClient client, NetworkStream stream)
    {
        try
        {
            while (ReferenceEquals(client, Volatile.Read(ref _tcp)))
            {
                var frame = await ChatFrame.ReadAsync(stream);
                if (frame == null) break;

                var (message, payload) = frame.Value;

                if (message.Type == "file")
                {
                    FileReceived?.Invoke(message, payload);
                }
                else
                {
                    MessageReceived?.Invoke(message);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException
                                   or ObjectDisposedException or InvalidDataException
                                   or JsonException)
        {
            System.Diagnostics.Debug.WriteLine($"UniChat: {ex.Message}");
        }
        finally
        {
            bool wasCurrent = ReferenceEquals(
                Interlocked.CompareExchange(ref _tcp, null, client),
                client);

            client.Dispose();

            if (wasCurrent)
            {
                Disconnected?.Invoke();
            }
        }
    }

    public void Disconnect()
    {
        Interlocked.Exchange(ref _tcp, null)?.Dispose();
    }
}