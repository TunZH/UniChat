using System.Buffers.Binary;
using System.Text.Json;

namespace UniChat.Common;

public static class ChatFrame
{
    private const int MaxHeaderBytes = 256 * 1024;
    public const int MaxPayloadBytes = 20 * 1024 * 1024;

    public static async Task WriteAsync(
        Stream stream,
        Message message,
        ReadOnlyMemory<byte> payload = default)
    {
        if (payload.Length > MaxPayloadBytes ||
            payload.Length != message.PayloadLength)
        {
            throw new InvalidDataException("Độ dài dữ liệu file không hợp lệ.");
        }

        byte[] header = JsonSerializer.SerializeToUtf8Bytes(message);

        if (header.Length > MaxHeaderBytes)
        {
            throw new InvalidDataException("Header vượt giới hạn kích thước.");
        }

        byte[] prefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(prefix, header.Length);

        await stream.WriteAsync(prefix);
        await stream.WriteAsync(header);

        if (!payload.IsEmpty)
        {
            await stream.WriteAsync(payload);
        }
    }

    public static async Task<(Message Header, byte[] Payload)?> ReadAsync(Stream stream)
    {
        byte[] prefix = new byte[sizeof(int)];
        int bytesRead = await stream.ReadAtLeastAsync(prefix, prefix.Length, false);

        if (bytesRead == 0)
        {
            return null;
        }

        if (bytesRead != prefix.Length)
        {
            throw new EndOfStreamException("Kết nối đóng giữa phần độ dài header.");
        }

        int headerLength = BinaryPrimitives.ReadInt32BigEndian(prefix);

        if (headerLength <= 0 || headerLength > MaxHeaderBytes)
        {
            throw new InvalidDataException("Độ dài header không hợp lệ.");
        }

        byte[] headerBytes = new byte[headerLength];
        await stream.ReadExactlyAsync(headerBytes);

        Message message = JsonSerializer.Deserialize<Message>(headerBytes)
            ?? throw new InvalidDataException("Header không có nội dung.");

        if (message.PayloadLength < 0 || message.PayloadLength > MaxPayloadBytes)
        {
            throw new InvalidDataException("Kích thước file không hợp lệ.");
        }

        byte[] payload = message.PayloadLength == 0
            ? Array.Empty<byte>()
            : new byte[message.PayloadLength];

        if (payload.Length > 0)
        {
            await stream.ReadExactlyAsync(payload);
        }

        return (message, payload);
    }
}