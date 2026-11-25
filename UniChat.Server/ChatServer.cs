using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using UniChat.Common;

namespace UniChat.Server;

public class ChatServer
{
    private readonly TcpListener _listener;
    private readonly ConcurrentDictionary<string, ClientSession> _clients = new();
    private readonly ConcurrentDictionary<string, byte> _knownUsers = new();
    private readonly ConcurrentDictionary<string, ChatGroupInfo> _groups = new();
    private readonly SemaphoreSlim _userListLock = new(1, 1);
    private readonly SemaphoreSlim _groupListLock = new(1, 1);

    private sealed class ClientSession(TcpClient client)
    {
        public NetworkStream Stream { get; } = client.GetStream();
        public SemaphoreSlim SendLock { get; } = new(1, 1);

        public void Close() => client.Close();
    }

    public ChatServer(int port)
    {
        _listener = new TcpListener(IPAddress.Any, port);
    }

    public async Task StartAsync()
    {
        _listener.Start();

        Console.WriteLine("=================================");
        Console.WriteLine("       UNICHAT SERVER");
        Console.WriteLine("=================================");
        Console.WriteLine("Server dang hoat dong!");
        Console.WriteLine($"Port: {((IPEndPoint)_listener.LocalEndpoint).Port}");
        Console.WriteLine("Dang cho Client ket noi...");

        while (true)
        {
            TcpClient client = await _listener.AcceptTcpClientAsync();
            _ = HandleClientAsync(client);
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        var session = new ClientSession(client);
        string? username = null;

        try
        {
            while (true)
            {
                var frame = await ChatFrame.ReadAsync(session.Stream);
                if (frame == null) break;

                var (message, payload) = frame.Value;

                if (message.Type != "file" && payload.Length > 0)
                {
                    await SendNoticeAsync(session, "Gói tin này không hỗ trợ dữ liệu file.");
                    continue;
                }

                if (message.Type == "login")
                {
                    if (username != null) continue;

                    var requestedName = message.From?.Trim();

                    if (string.IsNullOrWhiteSpace(requestedName) ||
                        requestedName == "all" ||
                        !_clients.TryAdd(requestedName, session))
                    {
                        await SendAsync(session, new Message
                        {
                            Type = "error",
                            Text = "Tên người dùng không hợp lệ hoặc đã được sử dụng."
                        });
                        return;
                    }

                    username = requestedName;
                    _knownUsers.TryAdd(username, 0);

                    Console.WriteLine($"[+] {username} ket noi");
                    await BroadcastAsync(new Message
                    {
                        Type = "system",
                        Text = $"{username} đã tham gia"
                    });

                    await BroadcastUserListAsync();
                    await SendGroupListAsync(username, session);
                    continue;
                }

                if (username == null) continue;

                switch (message.Type)
                {
                    case "msg":
                        message.From = username;

                        if (message.To == "all")
                        {
                            await BroadcastAsync(message);
                        }
                        else if (!string.IsNullOrEmpty(message.To) &&
                                 _clients.TryGetValue(message.To, out var target) &&
                                 await SendAsync(target, message))
                        {
                            if (target != session)
                            {
                                await SendAsync(session, message);
                            }
                        }
                        else
                        {
                            await SendNoticeAsync(session, "Người nhận đã mất kết nối.");
                        }

                        break;

                    case "create_group":
                        await CreateGroupAsync(username, session, message);
                        break;

                    case "group_msg":
                        if (!string.IsNullOrWhiteSpace(message.Text))
                        {
                            await SendToGroupAsync(username, session, message);
                        }

                        break;

                    case "file":
                        await ForwardFileAsync(username, session, message, payload);
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException
                                   or ObjectDisposedException or InvalidDataException
                                   or JsonException)
        {
            Console.WriteLine($"[CONNECTION] {ex.Message}");
        }
        finally
        {
            if (username != null)
            {
                _clients.TryRemove(username, out _);
            }

            session.Close();

            if (username != null)
            {
                Console.WriteLine($"[-] {username} thoat");
                await BroadcastAsync(new Message
                {
                    Type = "system",
                    Text = $"{username} đã rời đi"
                });
                await BroadcastUserListAsync();
            }
        }
    }

    private async Task CreateGroupAsync(
        string username, ClientSession session, Message request)
    {
        if (string.IsNullOrWhiteSpace(request.GroupName))
        {
            await SendNoticeAsync(session, "Bạn cần nhập tên nhóm.");
            return;
        }

        var members = (request.Members ?? new List<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name) && name != username)
            .Distinct()
            .ToList();

        if (members.Count == 0 || members.Any(name => !_knownUsers.ContainsKey(name)))
        {
            await SendNoticeAsync(session, "Hãy chọn ít nhất một thành viên hợp lệ.");
            return;
        }

        members.Insert(0, username);

        var group = new ChatGroupInfo
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = request.GroupName.Trim(),
            Members = members
        };

        _groups[group.Id] = group;
        Console.WriteLine($"[GROUP] {username} tao nhom {group.Name}");

        foreach (var member in group.Members)
        {
            if (_clients.TryGetValue(member, out var memberSession))
            {
                await SendGroupListAsync(member, memberSession);
            }
        }
    }

    private async Task SendToGroupAsync(
        string username,
        ClientSession sender,
        Message message,
        ReadOnlyMemory<byte> payload = default)
    {
        if (string.IsNullOrEmpty(message.GroupId) ||
            !_groups.TryGetValue(message.GroupId, out var group) ||
            !group.Members.Contains(username))
        {
            await SendNoticeAsync(sender, "Bạn không có quyền gửi vào nhóm này.");
            return;
        }

        message.From = username;
        message.GroupName = group.Name;
        message.To = "";

        foreach (var member in group.Members)
        {
            if (_clients.TryGetValue(member, out var memberSession))
            {
                await SendAsync(memberSession, message, payload);
            }
        }
    }

    private async Task ForwardFileAsync(
        string username, ClientSession sender, Message message, byte[] payload)
    {
        var fileName = Path.GetFileName((message.FileName ?? "").Replace('\\', '/'));
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        if (payload.Length == 0 ||
            extension is not (".png" or ".jpg" or ".jpeg" or ".mp4"))
        {
            await SendNoticeAsync(sender, "Hãy chọn ảnh PNG, JPG hoặc video MP4.");
            return;
        }

        var outgoing = new Message
        {
            Type = "file",
            From = username,
            To = message.To,
            GroupId = message.GroupId,
            FileName = fileName,
            PayloadLength = payload.Length
        };

        if (!string.IsNullOrEmpty(outgoing.GroupId))
        {
            await SendToGroupAsync(username, sender, outgoing, payload);
            return;
        }

        if (string.IsNullOrEmpty(outgoing.To) ||
            outgoing.To == "all" ||
            !_clients.TryGetValue(outgoing.To, out var recipient) ||
            !await SendAsync(recipient, outgoing, payload))
        {
            await SendNoticeAsync(sender, "Hãy chọn một người đang online để gửi file.");
            return;
        }

        if (recipient != sender)
        {
            await SendAsync(sender, outgoing, payload);
        }
    }

    private async Task SendGroupListAsync(string username, ClientSession session)
    {
        await _groupListLock.WaitAsync();

        try
        {
            await SendAsync(session, new Message
            {
                Type = "grouplist",
                Groups = _groups.Values
                    .Where(group => group.Members.Contains(username))
                    .OrderBy(group => group.Name)
                    .ThenBy(group => group.Id)
                    .ToList()
            });
        }
        finally
        {
            _groupListLock.Release();
        }
    }

    private async Task BroadcastUserListAsync()
    {
        await _userListLock.WaitAsync();

        try
        {
            await BroadcastAsync(new Message
            {
                Type = "userlist",
                Users = _clients.Keys.ToList(),
                KnownUsers = _knownUsers.Keys.OrderBy(name => name).ToList()
            });
        }
        finally
        {
            _userListLock.Release();
        }
    }

    private async Task BroadcastAsync(Message message)
    {
        foreach (var session in _clients.Values)
        {
            await SendAsync(session, message);
        }
    }

    private static Task<bool> SendNoticeAsync(ClientSession session, string text) =>
        SendAsync(session, new Message { Type = "system", Text = text });

    private static async Task<bool> SendAsync(
        ClientSession session,
        Message message,
        ReadOnlyMemory<byte> payload = default)
    {
        await session.SendLock.WaitAsync();

        try
        {
            await ChatFrame.WriteAsync(session.Stream, message, payload);
            return true;
        }
        catch (InvalidDataException ex)
        {
            Console.WriteLine($"[FRAME] {ex.Message}");
            return false;
        }
        catch (Exception ex) when (ex is IOException or SocketException
                                   or ObjectDisposedException)
        {
            session.Close();
            return false;
        }
        finally
        {
            session.SendLock.Release();
        }
    }
}