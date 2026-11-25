using System.Text.Json;

namespace UniChat.Common;

public class Message
{
    public string Type { get; set; } = "";
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Text { get; set; } = "";

    public List<string> Users { get; set; } = new();
    public List<string> KnownUsers { get; set; } = new();

    public string GroupId { get; set; } = "";
    public string GroupName { get; set; } = "";
    public List<string> Members { get; set; } = new();
    public List<ChatGroupInfo> Groups { get; set; } = new();

    public string FileName { get; set; } = "";
    public int PayloadLength { get; set; }

    public string ToJson() => JsonSerializer.Serialize(this);

    public static Message? FromJson(string json) =>
        JsonSerializer.Deserialize<Message>(json);
}

public class ChatGroupInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Members { get; set; } = new();
}