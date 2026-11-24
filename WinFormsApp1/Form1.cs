using Microsoft.Web.WebView2.WinForms;
using UniChat.Common;
using Message = UniChat.Common.Message;

namespace UniChat.Client;

public partial class Form1 : Form
{
    private static readonly Color Ink = Color.FromArgb(242, 243, 248);
    private static readonly Color Blue = Color.FromArgb(111, 51, 244);
    private static readonly Color Muted = Color.FromArgb(177, 181, 196);
    private static readonly Color Canvas = Color.FromArgb(24, 25, 29);
    private static readonly Color Surface = Color.FromArgb(34, 35, 41);
    private static readonly Color Incoming = Color.FromArgb(58, 59, 68);
    private static readonly Color Selected = Color.FromArgb(49, 47, 69);
    private static readonly string[] Emojis = ["❤️", "😆", "😮", "😢", "😡", "👍"];
    // Route text and reactions through the existing chat message types.
    private const string TextPrefix = "~UniChat/1/text/";
    private const string AttachmentPrefix = "UniChat-file-";
    private const string ReactionPrefix = "~UniChat/1/react/";

    // Use the server's LAN address here when clients run on different computers.
    private const string ServerHost = "127.0.0.1";
    private const int ServerPort = 5000;

    private readonly ChatClient _client = new();
    private readonly Panel _joinScreen = new();
    private readonly Label _joinStatus = new();
    private TableLayoutPanel _chatScreen = null!;
    private readonly FlowLayoutPanel _timeline = new();
    private readonly Label _status = new();
    private readonly TextBox _search = new();
    private readonly Label _conversationTitle = new();
    private readonly Button _attach = new();
    private readonly Button _createGroup = new();
    private readonly Dictionary<ConversationKey, List<ChatEntry>> _history = new();
    private readonly Queue<Attachment> _files = new();
    private List<string> _knownUsers = new();
    private HashSet<string> _online = new();
    private List<ChatGroupInfo> _groups = new();
    private string _me = "";
    private bool _connected;
    private bool _closing;
    private bool _busy;
    private bool _refreshing;
    private long _fileBytes;

    private readonly record struct ConversationKey(string Kind, string Id);
    private static readonly ConversationKey PublicChat = new("all", "");
    private sealed record Recipient(ConversationKey Key, string Name, bool Online)
    {
        public override string ToString() => Name;
    }

    private sealed record ChatEntry(string Author, string Text, DateTime Time,
        Attachment? File = null, bool Notice = false, Guid? Id = null)
    {
        public Dictionary<string, string> Reactions { get; } = new();
        public event Action? ReactionsChanged;

        public void UpdateReaction(string author, string emoji)
        {
            if (emoji.Length == 0) Reactions.Remove(author);
            else Reactions[author] = emoji;
            ReactionsChanged?.Invoke();
        }
    }

    private sealed class Attachment(string name, byte[] bytes)
    {
        public string Name { get; } = name;
        public byte[]? Bytes { get; private set; } = bytes;
        public Bitmap? Thumbnail { get; set; }

        public void Release()
        {
            Bytes = null;
            Thumbnail?.Dispose();
            Thumbnail = null;
        }

        public bool IsVideo => Path.GetExtension(Name).Equals(".mp4", StringComparison.OrdinalIgnoreCase);
    }

    public Form1()
    {
        InitializeComponent();
        BuildLayout();
        Shown += (_, _) => txtUser.Focus();

        _client.MessageReceived += message => PostToUi(() => ReceiveMessage(message));
        _client.FileReceived += (message, payload) => PostToUi(() => ReceiveFile(message, payload));
        _client.Disconnected += () => PostToUi(Disconnected);

        RefreshRecipients();
        UpdateComposer();
    }

    private void BuildLayout()
    {
        var reused = new HashSet<Control>
        {
            lblIP, lblPort, lblUser, lblOnline, txtIP, txtPort, txtUser,
            txtMessage, btnConnect, btnSend, lstUsers
        };

        foreach (Control control in Controls.Cast<Control>().ToArray())
        {
            Controls.Remove(control);
            if (!reused.Contains(control)) control.Dispose();
        }

        Text = "UniChat";
        Font = new Font("Segoe UI", 10F);
        BackColor = Surface;
        ForeColor = Ink;
        ClientSize = new Size(1040, 690);
        MinimumSize = new Size(820, 520);
        StartPosition = FormStartPosition.CenterScreen;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        _chatScreen = root;
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var connection = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            Padding = new Padding(15, 15, 10, 8),
            Margin = Padding.Empty,
            BackColor = Surface,
            ForeColor = Color.White
        };

        var brand = new Label
        {
            Text = "UniChat",
            AutoSize = true,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 17F, FontStyle.Bold),
            Margin = new Padding(0, 0, 18, 0)
        };

        _status.AutoSize = true;
        _status.Margin = new Padding(4, 9, 0, 0);
        _status.ForeColor = Color.FromArgb(196, 211, 235);
        _status.Text = "Chưa kết nối";

        connection.Controls.AddRange(new Control[]
        {
            brand, _status
        });

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 2, RowCount = 1 };
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 285));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var sidebar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(13, 12, 13, 13),
            Margin = Padding.Empty,
            BackColor = Surface
        };
        sidebar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));

        lblOnline.Text = "Cuộc trò chuyện";
        lblOnline.AutoSize = true;
        lblOnline.Font = new Font("Segoe UI", 13F, FontStyle.Bold);
        lblOnline.Margin = new Padding(4, 1, 0, 0);

        lstUsers.Dock = DockStyle.Fill;
        lstUsers.Margin = Padding.Empty;
        lstUsers.BorderStyle = BorderStyle.None;
        lstUsers.BackColor = Surface;
        lstUsers.IntegralHeight = false;
        lstUsers.DrawMode = DrawMode.OwnerDrawFixed;
        lstUsers.ItemHeight = 53;
        lstUsers.DrawItem += DrawRecipient;
        lstUsers.SelectedIndexChanged += (_, _) =>
        {
            if (_refreshing) return;
            RenderChat();
            UpdateComposer();
        };

        ConfigureButton(_createGroup, "+ Tạo nhóm");
        _createGroup.Dock = DockStyle.Fill;
        _createGroup.Margin = new Padding(2, 5, 2, 0);
        _createGroup.Click += CreateGroup_Click;

        sidebar.Controls.Add(lblOnline, 0, 0);
        ConfigureInput(_search, 230);
        _search.Dock = DockStyle.Fill;
        _search.PlaceholderText = "Tìm cuộc trò chuyện";
        _search.AccessibleName = "Tìm cuộc trò chuyện";
        _search.TextChanged += (_, _) => RefreshRecipients();
        sidebar.Controls.Add(_search, 0, 1);
        sidebar.Controls.Add(lstUsers, 0, 2);
        sidebar.Controls.Add(_createGroup, 0, 3);

        var conversation = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Canvas
        };
        conversation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        conversation.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        conversation.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        conversation.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));

        _conversationTitle.Dock = DockStyle.Fill;
        _conversationTitle.AutoSize = false;
        _conversationTitle.Margin = Padding.Empty;
        _conversationTitle.Padding = new Padding(17, 0, 0, 0);
        _conversationTitle.TextAlign = ContentAlignment.MiddleLeft;
        _conversationTitle.BackColor = Selected;
        _conversationTitle.Font = new Font("Segoe UI", 13F, FontStyle.Bold);

        _timeline.Dock = DockStyle.Fill;
        _timeline.Margin = Padding.Empty;
        _timeline.Padding = new Padding(12, 10, 12, 10);
        _timeline.AutoScroll = true;
        _timeline.WrapContents = false;
        _timeline.FlowDirection = FlowDirection.TopDown;
        _timeline.BackColor = Canvas;
        _timeline.SizeChanged += (_, _) => RenderChat();

        var composer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(12, 10, 12, 10),
            BackColor = Surface
        };
        composer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        composer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
        composer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        composer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));

        var messageField = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(3, 0, 3, 0),
            BackColor = Incoming
        };
        ConfigureInput(txtMessage, 400);
        txtMessage.BorderStyle = BorderStyle.None;
        txtMessage.PlaceholderText = "Viết tin nhắn…";
        txtMessage.AccessibleName = "Nội dung tin nhắn";
        messageField.Controls.Add(txtMessage);
        messageField.Resize += (_, _) =>
        {
            txtMessage.SetBounds(15, Math.Max(0, (messageField.Height - txtMessage.Height) / 2),
                Math.Max(0, messageField.Width - 30), txtMessage.Height);
            RoundBubble(messageField, messageField.Height);
        };

        ConfigureComposerIcon(_attach, "Đính kèm", DrawAttachIcon);
        ConfigureComposerIcon(btnSend, "Gửi tin nhắn", DrawSendIcon);
        _attach.Click += Attach_Click;

        composer.Controls.Add(_attach, 0, 0);
        composer.Controls.Add(messageField, 1, 0);
        composer.Controls.Add(btnSend, 2, 0);
        conversation.Controls.Add(_conversationTitle, 0, 0);
        conversation.Controls.Add(_timeline, 0, 1);
        conversation.Controls.Add(composer, 0, 2);
        body.Controls.Add(sidebar, 0, 0);
        body.Controls.Add(conversation, 1, 0);
        root.Controls.Add(connection, 0, 0);
        root.Controls.Add(body, 0, 1);
        Controls.Add(root);
        BuildJoinScreen();
        ShowJoinScreen();
    }

    private static void ConfigureComposerIcon(Button button, string name, Action<Graphics, Color> draw)
    {
        ConfigureButton(button, "");
        button.AccessibleName = name;
        button.Dock = DockStyle.Fill;
        button.Margin = Padding.Empty;
        button.BackColor = Surface;
        button.FlatAppearance.MouseOverBackColor = Selected;
        button.FlatAppearance.MouseDownBackColor = Incoming;
        button.Paint += (_, e) =>
        {
            var state = e.Graphics.Save();
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.TranslateTransform((button.ClientSize.Width - 24) / 2f,
                (button.ClientSize.Height - 24) / 2f);
            draw(e.Graphics, button.Enabled ? Color.FromArgb(56, 150, 255) : Muted);
            e.Graphics.Restore(state);
        };
        button.EnabledChanged += (_, _) => button.Invalidate();
    }

    private static void DrawSendIcon(Graphics graphics, Color color)
    {
        using var brush = new SolidBrush(color);
        graphics.FillPolygon(brush, new PointF[]
        {
            new(2, 2), new(23, 12), new(2, 22), new(5, 13), new(15, 12), new(5, 11)
        });
    }

    private static void DrawAttachIcon(Graphics graphics, Color color)
    {
        using var brush = new SolidBrush(color);
        using var pen = new Pen(Surface, 2);
        graphics.FillEllipse(brush, 2, 2, 20, 20);
        graphics.DrawLine(pen, 7, 12, 17, 12);
        graphics.DrawLine(pen, 12, 7, 12, 17);
    }

    // Noto Color Emoji artwork, rendered to PNG. See the notice bundled with this update.
    private static readonly Dictionary<string, Bitmap> ReactionIcons = new()
    {
        ["❤️"] = DecodeReactionIcon("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"),
        ["😆"] = DecodeReactionIcon("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"),
        ["😮"] = DecodeReactionIcon("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"),
        ["😢"] = DecodeReactionIcon("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"),
        ["😡"] = DecodeReactionIcon("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"),
        ["👍"] = DecodeReactionIcon("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"),
    };

    private static Bitmap DecodeReactionIcon(string png)
    {
        using var stream = new MemoryStream(Convert.FromBase64String(png));
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }

    private static void DrawReactionIcon(Graphics graphics, string emoji, int width, int height, int size)
    {
        var icon = ReactionIcons[emoji];
        float scale = (float)size / Math.Max(icon.Width, icon.Height);
        int iconWidth = (int)Math.Round(icon.Width * scale);
        int iconHeight = (int)Math.Round(icon.Height * scale);
        graphics.DrawImage(icon, (width - iconWidth) / 2, (height - iconHeight) / 2, iconWidth, iconHeight);
    }

    private void BuildJoinScreen()
    {
        _joinScreen.Dock = DockStyle.Fill;
        _joinScreen.BackColor = Canvas;

        var card = new TableLayoutPanel
        {
            Width = 390,
            Height = 315,
            BackColor = Surface,
            Padding = new Padding(28),
            ColumnCount = 1,
            RowCount = 6
        };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (int height in new[] { 48, 40, 27, 37, 47, 48 })
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, height));

        var title = new Label
        {
            Text = "UniChat",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 25F, FontStyle.Bold)
        };
        var subtitle = new Label
        {
            Text = "Nhập tên để bắt đầu trò chuyện.",
            Dock = DockStyle.Fill,
            ForeColor = Muted
        };
        lblUser.Text = "Tên hiển thị";
        lblUser.Dock = DockStyle.Fill;
        lblUser.ForeColor = Ink;

        ConfigureInput(txtUser, 320);
        txtUser.Dock = DockStyle.Fill;
        txtUser.Text = "";
        txtUser.PlaceholderText = "Ví dụ: Tuấn";
        txtUser.AccessibleName = "Tên hiển thị";

        ConfigureButton(btnConnect, "Vào UniChat", true);
        btnConnect.Dock = DockStyle.Fill;
        btnConnect.ForeColor = Color.White;
        btnConnect.Margin = new Padding(0, 4, 0, 4);
        _joinStatus.Dock = DockStyle.Fill;
        _joinStatus.ForeColor = Muted;
        _joinStatus.TextAlign = ContentAlignment.MiddleLeft;

        card.Controls.Add(title, 0, 0);
        card.Controls.Add(subtitle, 0, 1);
        card.Controls.Add(lblUser, 0, 2);
        card.Controls.Add(txtUser, 0, 3);
        card.Controls.Add(btnConnect, 0, 4);
        card.Controls.Add(_joinStatus, 0, 5);

        _joinScreen.Controls.Add(card);
        _joinScreen.Resize += (_, _) =>
        {
            card.Left = (_joinScreen.ClientSize.Width - card.Width) / 2;
            card.Top = (_joinScreen.ClientSize.Height - card.Height) / 2;
        };
        Controls.Add(_joinScreen);
    }

    private void ShowJoinScreen()
    {
        _chatScreen.Hide();
        _search.Clear();
        _joinScreen.Show();
        _joinScreen.BringToFront();
        txtUser.Enabled = true;
        btnConnect.Enabled = true;
        AcceptButton = btnConnect;
        txtUser.Focus();
    }

    private static void ConfigureInput(TextBox box, int width)
    {
        box.Width = width;
        box.Margin = new Padding(0, 5, 2, 0);
        box.ForeColor = Ink;
        box.BackColor = Incoming;
        box.BorderStyle = BorderStyle.FixedSingle;
    }

    private static void ConfigureButton(Button button, string text, bool primary = false)
    {
        button.Text = text;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.UseVisualStyleBackColor = false;
        button.BackColor = primary ? Blue : Selected;
        button.ForeColor = primary ? Color.White : Ink;
        button.Cursor = Cursors.Hand;
    }

    private Recipient? SelectedRecipient => lstUsers.SelectedItem as Recipient;

    private void DrawRecipient(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || lstUsers.Items[e.Index] is not Recipient recipient) return;

        bool selected = (e.State & DrawItemState.Selected) != 0;
        using var background = new SolidBrush(selected ? Selected : Surface);
        e.Graphics.FillRectangle(background, e.Bounds);

        var avatar = new Rectangle(e.Bounds.X + 8, e.Bounds.Y + 10, 33, 33);
        using var fill = new SolidBrush(Selected);
        e.Graphics.FillEllipse(fill, avatar);
        var initial = recipient.Key.Kind == "all" ? "#" :
            recipient.Key.Kind == "group" ? "G" : recipient.Name[..1].ToUpperInvariant();
        TextRenderer.DrawText(e.Graphics, initial, Font, avatar, Ink,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        var name = new Rectangle(e.Bounds.X + 49, e.Bounds.Y + 5, e.Bounds.Width - 52, 23);
        var detail = new Rectangle(name.X, name.Bottom, name.Width, 19);
        var caption = recipient.Key.Kind switch
        {
            "all" => "Phòng chung",
            "group" => "Trò chuyện nhóm",
            _ => recipient.Online ? "● Đang hoạt động" : "○ Ngoại tuyến"
        };

        TextRenderer.DrawText(e.Graphics, recipient.Name, Font, name, Ink, TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(e.Graphics, caption, Font, detail,
            recipient.Key.Kind == "user" && recipient.Online ? Color.FromArgb(112, 222, 163) : Muted,
            TextFormatFlags.EndEllipsis);
        e.DrawFocusRectangle();
    }

    private void RefreshRecipients()
    {
        var previous = SelectedRecipient?.Key ?? PublicChat;
        _refreshing = true;
        lstUsers.BeginUpdate();

        try
        {
            lstUsers.Items.Clear();
            string query = _search.Text.Trim();
            bool Matches(string name) => name.Contains(query, StringComparison.CurrentCultureIgnoreCase);

            if (Matches("Tất cả") || Matches("Phòng chung"))
                lstUsers.Items.Add(new Recipient(PublicChat, "Tất cả", true));

            foreach (var name in _knownUsers.Where(name => name.Length > 0 && name != _me && Matches(name))
                         .OrderByDescending(_online.Contains).ThenBy(name => name))
            {
                lstUsers.Items.Add(new Recipient(new ConversationKey("user", name), name, _online.Contains(name)));
            }

            foreach (var group in _groups.Where(group => Matches(group.Name)))
            {
                lstUsers.Items.Add(new Recipient(new ConversationKey("group", group.Id), group.Name, true));
            }

            int selected = lstUsers.Items.Count == 0 ? -1 : 0;
            for (int i = 0; i < lstUsers.Items.Count; i++)
            {
                if (lstUsers.Items[i] is Recipient item && item.Key == previous)
                {
                    selected = i;
                    break;
                }
            }

            lstUsers.SelectedIndex = selected;
        }
        finally
        {
            lstUsers.EndUpdate();
            _refreshing = false;
        }

        RenderChat();
        UpdateComposer();
    }

    private void UpdateComposer()
    {
        var recipient = SelectedRecipient;
        bool canSend = _connected && !_busy && recipient is { Online: true };

        txtMessage.Enabled = canSend;
        txtMessage.PlaceholderText = recipient is { Online: false }
            ? "Người dùng đang ngoại tuyến" : "Viết tin nhắn…";
        btnSend.Enabled = canSend;
        _attach.Enabled = canSend && recipient?.Key.Kind != "all";
        _createGroup.Enabled = _connected && !_busy;

        AcceptButton = _connected ? btnSend : btnConnect;
    }

    private async void btnConnect_Click(object? sender, EventArgs e)
    {
        var name = txtUser.Text.Trim();

        if (name.Length == 0)
        {
            _joinStatus.Text = "Bạn cần nhập tên hiển thị.";
            txtUser.Focus();
            return;
        }

        _me = name;
        btnConnect.Enabled = false;
        txtUser.Enabled = false;
        _joinStatus.Text = "Đang kết nối…";

        try
        {
            await _client.ConnectAsync(ServerHost, ServerPort, name);
        }
        catch (Exception)
        {
            if (_closing) return;
            _client.Disconnect();
            Disconnected();
            _joinStatus.Text = "Không thể vào UniChat. Vui lòng thử lại.";
        }
    }

    private async void btnSend_Click(object? sender, EventArgs e)
    {
        var recipient = SelectedRecipient;
        var text = txtMessage.Text.Trim();

        if (!_connected || _busy || recipient is not { Online: true } || text.Length == 0) return;

        var message = CreateChatMessage(recipient.Key, $"{TextPrefix}{Guid.NewGuid():N}/{text}");
        await RunSendAsync(async () =>
        {
            await _client.SendAsync(message);
            txtMessage.Clear();
        });
    }

    private static Message CreateChatMessage(ConversationKey key, string text)
    {
        var message = new Message { Text = text };
        if (key.Kind == "group")
        {
            message.Type = "group_msg";
            message.GroupId = key.Id;
        }
        else
        {
            message.Type = "msg";
            message.To = key.Kind == "user" ? key.Id : "all";
        }
        return message;
    }

    private async void Attach_Click(object? sender, EventArgs e)
    {
        var recipient = SelectedRecipient;
        if (!_connected || _busy || recipient is not { Online: true } || recipient.Key.Kind == "all") return;

        using var picker = new OpenFileDialog
        {
            Title = "Chọn ảnh hoặc video",
            Filter = "Ảnh và video|*.png;*.jpg;*.jpeg;*.mp4",
            CheckFileExists = true
        };

        if (picker.ShowDialog(this) != DialogResult.OK || !_connected) return;

        var name = Path.GetFileName(picker.FileName);
        var extension = Path.GetExtension(name).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".mp4"))
        {
            MessageBox.Show(this, "Hãy chọn ảnh PNG, JPG/JPEG hoặc video MP4.");
            return;
        }

        await RunSendAsync(async () =>
        {
            using var stream = File.OpenRead(picker.FileName);
            var length = stream.Length;

            if (length <= 0 || length > ChatFrame.MaxPayloadBytes)
            {
                MessageBox.Show(this, "Tệp phải có dữ liệu và tối đa 20 MiB.");
                return;
            }

            var data = new byte[(int)length];
            await stream.ReadExactlyAsync(data);
            await _client.SendAsync(new Message
            {
                Type = "file",
                To = recipient.Key.Kind == "user" ? recipient.Key.Id : "",
                GroupId = recipient.Key.Kind == "group" ? recipient.Key.Id : "",
                FileName = $"{AttachmentPrefix}{Guid.NewGuid():N}_{name}",
                PayloadLength = data.Length
            }, data);
        });
    }

    private async Task RunSendAsync(Func<Task> action)
    {
        _busy = true;
        UpdateComposer();

        try
        {
            await action();
        }
        catch (Exception ex)
        {
            if (!_closing) MessageBox.Show(this, "Không gửi được. " + ex.Message);
        }
        finally
        {
            _busy = false;
            if (!_closing) UpdateComposer();
        }
    }

    private async void CreateGroup_Click(object? sender, EventArgs e)
    {
        if (!_connected || _busy) return;

        var candidates = _knownUsers.Where(name => name != _me).ToArray();
        if (candidates.Length == 0)
        {
            MessageBox.Show(this, "Hãy kết nối thêm ít nhất một người dùng.");
            return;
        }

        using var dialog = new Form
        {
            Text = "Tạo nhóm",
            ClientSize = new Size(390, 340),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            Font = Font,
            BackColor = Surface,
            ForeColor = Ink
        };

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(14) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        var groupName = new TextBox { Dock = DockStyle.Fill, PlaceholderText = "Ví dụ Nhóm Lập trình mạng" };
        var members = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true };
        members.Items.AddRange(candidates);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var create = new Button { Size = new Size(108, 32) };
        var cancel = new Button { Size = new Size(76, 32), DialogResult = DialogResult.Cancel };
        ConfigureButton(create, "Tạo nhóm", true);
        ConfigureButton(cancel, "Hủy");
        create.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(groupName.Text) || members.CheckedItems.Count == 0)
            {
                MessageBox.Show(dialog, "Nhập tên nhóm và chọn ít nhất một thành viên.");
                return;
            }

            dialog.DialogResult = DialogResult.OK;
        };

        actions.Controls.Add(create);
        actions.Controls.Add(cancel);
        layout.Controls.Add(new Label { Text = "Tên nhóm", AutoSize = true }, 0, 0);
        layout.Controls.Add(groupName, 0, 1);
        layout.Controls.Add(new Label { Text = "Chọn thành viên", AutoSize = true }, 0, 2);
        layout.Controls.Add(members, 0, 3);
        layout.Controls.Add(actions, 0, 4);
        dialog.Controls.Add(layout);
        dialog.AcceptButton = create;
        dialog.CancelButton = cancel;

        if (dialog.ShowDialog(this) != DialogResult.OK || !_connected) return;

        await RunSendAsync(() => _client.SendAsync(new Message
        {
            Type = "create_group",
            GroupName = groupName.Text.Trim(),
            Members = members.CheckedItems.Cast<string>().ToList()
        }));
    }

    private ConversationKey ResolveConversation(Message message)
    {
        if (!string.IsNullOrEmpty(message.GroupId)) return new("group", message.GroupId);
        if (message.To == "all") return PublicChat;
        return new("user", message.From == _me ? message.To : message.From);
    }

    private void ReceiveMessage(Message message)
    {
        switch (message.Type)
        {
            case "msg":
            case "group_msg":
                ReceiveChatText(message);
                break;
            case "system":
                // Join and leave notices belong in the common room. Action feedback follows the selected chat.
                bool presence = message.Text.EndsWith(" đã tham gia", StringComparison.Ordinal)
                    || message.Text.EndsWith(" đã rời đi", StringComparison.Ordinal)
                    || message.Text.EndsWith(" da tham gia", StringComparison.Ordinal)
                    || message.Text.EndsWith(" da roi di", StringComparison.Ordinal);
                AddEntry(presence ? PublicChat : SelectedRecipient?.Key ?? PublicChat,
                    new ChatEntry("", message.Text, DateTime.Now, Notice: true));
                break;
            case "error":
                MessageBox.Show(this, message.Text);
                break;
            case "userlist":
                if (!_connected)
                {
                    _connected = true;
                    _joinScreen.Hide();
                    _chatScreen.Show();
                    _chatScreen.BringToFront();
                }
                _status.Text = "● Online";
                _status.ForeColor = Color.FromArgb(150, 241, 196);
                _knownUsers = message.KnownUsers;
                _online = new HashSet<string>(message.Users);
                RefreshRecipients();
                break;
            case "grouplist":
                _groups = message.Groups;
                RefreshRecipients();
                break;
        }
    }

    private void ReceiveChatText(Message message)
    {
        var key = ResolveConversation(message);
        var text = message.Text;
        if (TryReadChatEnvelope(text, ReactionPrefix, out var target, out var emoji)
            && (emoji.Length == 0 || Emojis.Contains(emoji)))
        {
            if (_history.TryGetValue(key, out var entries)
                && entries.Find(entry => entry.Id == target) is { } entry)
            {
                entry.UpdateReaction(message.From, emoji);
            }
            return;
        }

        if (TryReadChatEnvelope(text, TextPrefix, out var id, out var body))
            AddEntry(key, new ChatEntry(message.From, body, DateTime.Now, Id: id));
        else
            AddEntry(key, new ChatEntry(message.From, text, DateTime.Now));
    }

    private static bool TryReadChatEnvelope(string text, string prefix, out Guid id, out string body)
    {
        id = default;
        body = "";
        int separator = prefix.Length + 32;
        if (!text.StartsWith(prefix, StringComparison.Ordinal) || text.Length <= separator
            || text[separator] != '/' || !Guid.TryParseExact(text.AsSpan(prefix.Length, 32), "N", out id))
            return false;

        body = text[(separator + 1)..];
        return true;
    }

    private void ShowReactionMenu(Control anchor, ConversationKey key, ChatEntry entry, Guid id)
    {
        if (!_connected || _busy || SelectedRecipient is not { Online: true }) return;

        var menu = new ContextMenuStrip { BackColor = Surface, ForeColor = Ink, ShowImageMargin = false };
        var options = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(4),
            Margin = Padding.Empty,
            BackColor = Surface
        };

        foreach (string emoji in Emojis)
        {
            var button = new Button { Size = new Size(36, 36), AccessibleName = emoji };
            ConfigureButton(button, "");
            button.Paint += (_, e) => DrawReactionIcon(e.Graphics, emoji,
                button.ClientSize.Width, button.ClientSize.Height, 25);
            button.Click += async (_, _) =>
            {
                // Let WinForms finish the hosted button's click before closing its menu.
                BeginInvoke((Action)(() =>
                {
                    if (!menu.IsDisposed && menu.Visible) menu.Close();
                }));
                string value = entry.Reactions.TryGetValue(_me, out var current) && current == emoji ? "" : emoji;
                if (_connected && !_busy)
                    await RunSendAsync(() => _client.SendAsync(
                        CreateChatMessage(key, $"{ReactionPrefix}{id:N}/{value}")));
            };
            options.Controls.Add(button);
        }

        menu.Items.Add(new ToolStripControlHost(options) { Padding = Padding.Empty, Margin = Padding.Empty });
        menu.Closed += (_, _) => BeginInvoke((Action)menu.Dispose);
        menu.Show(anchor, new Point(0, anchor.Height));
    }

    private void ReceiveFile(Message message, byte[] payload)
    {
        var name = Path.GetFileName(message.FileName);
        if (string.IsNullOrWhiteSpace(name) || payload.Length == 0) return;

        // The existing server forwards the file name with the payload.
        // Remove the transport ID before using the original name in the client.
        Guid? id = null;
        int separator = AttachmentPrefix.Length + 32;
        if (name.StartsWith(AttachmentPrefix, StringComparison.Ordinal)
            && name.Length > separator + 1 && name[separator] == '_'
            && Guid.TryParseExact(name.AsSpan(AttachmentPrefix.Length, 32), "N", out var parsed))
        {
            id = parsed;
            name = name[(separator + 1)..];
        }

        var file = new Attachment(name, payload);
        if (!file.IsVideo) file.Thumbnail = MakeThumbnail(payload, 260, 145);
        _files.Enqueue(file);
        _fileBytes += payload.Length;

        bool removedOldFile = false;
        while (_files.Count > 30 || _fileBytes > 60L * 1024 * 1024)
        {
            var oldFile = _files.Dequeue();
            _fileBytes -= oldFile.Bytes!.Length;
            oldFile.Release();
            removedOldFile = true;
        }

        var key = ResolveConversation(message);
        AddEntry(key, new ChatEntry(message.From, "", DateTime.Now, file, Id: id));

        if (removedOldFile) RenderChat();
    }

    private void AddEntry(ConversationKey key, ChatEntry entry)
    {
        if (!_history.TryGetValue(key, out var entries))
        {
            entries = new List<ChatEntry>();
            _history.Add(key, entries);
        }

        entries.Add(entry);
        bool trimmed = entries.Count > 120;
        if (trimmed) entries.RemoveAt(0);
        if (SelectedRecipient?.Key != key) return;

        int width = ChatRowWidth();
        if (entries.Count == 1 || trimmed || width < 70)
        {
            RenderChat();
            return;
        }

        AddChatRow(entry, width);
        ScrollToLatest();
    }

    private void RenderChat()
    {
        if (_closing || _timeline.ClientSize.Width < 100) return;

        var recipient = SelectedRecipient;
        _conversationTitle.Text = recipient?.Name ?? "Cuộc trò chuyện";
        _timeline.SuspendLayout();

        try
        {
            while (_timeline.Controls.Count > 0) _timeline.Controls[0].Dispose();

            int width = ChatRowWidth();
            if (width < 70) return;

            if (recipient != null && _history.TryGetValue(recipient.Key, out var entries))
            {
                foreach (var entry in entries) AddChatRow(entry, width);
            }
            else
            {
                var text = _connected
                    ? recipient == null ? "Không tìm thấy cuộc trò chuyện." : "Chưa có tin nhắn trong cuộc trò chuyện này."
                    : "Nhập tên và kết nối để bắt đầu trò chuyện.";
                AddChatRow(new ChatEntry("", text, DateTime.Now, Notice: true), width);
            }
        }
        finally
        {
            _timeline.ResumeLayout(true);
        }

        ScrollToLatest();
    }

    private int ChatRowWidth() => _timeline.ClientSize.Width - _timeline.Padding.Horizontal
        - SystemInformation.VerticalScrollBarWidth - 4;

    private void ScrollToLatest()
    {
        if (_timeline.Controls.Count > 0)
        {
            _timeline.ScrollControlIntoView(_timeline.Controls[_timeline.Controls.Count - 1]);
        }
    }

    private void AddChatRow(ChatEntry entry, int width)
    {
        bool mine = !entry.Notice && entry.Author == _me;
        int maxWidth = Math.Min(410, Math.Max(48, width * 3 / 4));

        var row = new FlowLayoutPanel
        {
            Width = width,
            Margin = new Padding(0, 0, 0, 8),
            WrapContents = false,
            FlowDirection = mine ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            TabStop = false
        };

        var bubble = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = entry.Notice ? Canvas : mine ? Blue : Incoming,
            ForeColor = entry.Notice ? Muted : mine ? Color.White : Ink,
            Padding = new Padding(10, 7, 10, 7),
            Margin = Padding.Empty,
            Font = Font
        };

        if (!entry.Notice)
        {
            var label = mine ? $"Bạn   ·   {entry.Time:HH:mm}" : $"{entry.Author}   ·   {entry.Time:HH:mm}";
            bubble.Controls.Add(BubbleLabel(label, maxWidth, mine ? Color.White : Muted));
        }

        if (entry.File is { } file)
        {
            if (file.Bytes == null)
            {
                bubble.Controls.Add(BubbleLabel("Tệp đã hết thời gian giữ trong phiên.", maxWidth, bubble.ForeColor));
            }
            else
            {
                if (file.IsVideo)
                {
                    bubble.Controls.Add(CreateInlineVideo(file, maxWidth));
                }
                else if (file.Thumbnail is { } thumbnail)
                {
                    var image = new PictureBox
                    {
                        Image = thumbnail,
                        Size = new Size(Math.Min(thumbnail.Width, maxWidth),
                            Math.Max(1, thumbnail.Height * Math.Min(thumbnail.Width, maxWidth) / thumbnail.Width)),
                        Cursor = Cursors.Hand,
                        SizeMode = PictureBoxSizeMode.Zoom
                    };
                    image.Click += (_, _) => ShowImage(file);
                    bubble.Controls.Add(image);
                }

                var actions = new FlowLayoutPanel
                {
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    WrapContents = false,
                    Margin = new Padding(0, 2, 0, 0)
                };

                if (!file.IsVideo && file.Thumbnail == null)
                    bubble.Controls.Add(BubbleLabel("Không xem trước được ảnh này.", maxWidth, bubble.ForeColor));

                var save = new Button { AutoSize = true };
                ConfigureButton(save, file.IsVideo ? "Lưu video" : "Lưu ảnh");
                save.Click += async (_, _) => await SaveFileAsync(file);
                actions.Controls.Add(save);
                bubble.Controls.Add(actions);
            }
        }
        else
        {
            bubble.Controls.Add(BubbleLabel(entry.Text, maxWidth, bubble.ForeColor));
        }

        if (entry.Id is Guid id && SelectedRecipient is { } recipient)
        {
            var react = new Button
            {
                Size = new Size(34, 30),
                Margin = new Padding(5, 3, 5, 0),
                AccessibleName = "Thả cảm xúc",
                Font = new Font("Segoe UI Emoji", 10F)
            };
            ConfigureButton(react, "☺");
            react.Click += (_, _) => ShowReactionMenu(react, recipient.Key, entry, id);
            foreach (Control child in bubble.Controls)
                child.MouseUp += (_, e) =>
                {
                    if (e.Button == MouseButtons.Right) ShowReactionMenu(react, recipient.Key, entry, id);
                };

            FlowLayoutPanel? badge = null;
            void RefreshReactionBadge()
            {
                badge?.Dispose();
                badge = null;
                if (entry.Reactions.Count == 0) return;

                badge = new FlowLayoutPanel
                {
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    WrapContents = false,
                    BackColor = Surface,
                    Margin = new Padding(0, 3, 0, 0),
                    Padding = new Padding(4, 1, 4, 1),
                    Cursor = Cursors.Hand
                };
                badge.Click += (_, _) => ShowReactionMenu(react, recipient.Key, entry, id);
                foreach (var group in entry.Reactions.Values.GroupBy(value => value))
                {
                    var emoji = group.Key;
                    var symbol = new Panel
                    {
                        Size = new Size(21, 21),
                        Margin = Padding.Empty,
                        AccessibleName = emoji
                    };
                    symbol.Paint += (_, e) => DrawReactionIcon(e.Graphics, emoji,
                        symbol.ClientSize.Width, symbol.ClientSize.Height, 18);
                    symbol.Click += (_, _) => ShowReactionMenu(react, recipient.Key, entry, id);
                    badge.Controls.Add(symbol);
                    if (group.Count() > 1)
                    {
                        var count = BubbleLabel(group.Count().ToString(), 32, Ink);
                        count.Margin = new Padding(1, 0, 2, 0);
                        count.Click += (_, _) => ShowReactionMenu(react, recipient.Key, entry, id);
                        badge.Controls.Add(count);
                    }
                }
                badge.SizeChanged += (_, _) => RoundBubble(badge, badge.Height);
                bubble.Controls.Add(badge);
            }
            entry.ReactionsChanged += RefreshReactionBadge;
            row.Disposed += (_, _) => entry.ReactionsChanged -= RefreshReactionBadge;
            RefreshReactionBadge();
            row.Controls.Add(bubble);
            row.Controls.Add(react);
        }
        else
        {
            row.Controls.Add(bubble);
        }

        if (!entry.Notice)
        {
            bubble.SizeChanged += (_, _) => RoundBubble(bubble);
            RoundBubble(bubble);
        }
        bubble.SizeChanged += (_, _) => row.Height = Math.Max(36, bubble.Height + 4);
        row.Height = Math.Max(36, Math.Max(bubble.Height, bubble.GetPreferredSize(Size.Empty).Height) + 4);
        _timeline.Controls.Add(row);
    }

    private static void RoundBubble(Control bubble, int cornerDiameter = 24)
    {
        int width = bubble.Width;
        int height = bubble.Height;
        int diameter = Math.Min(cornerDiameter, Math.Min(width, height));
        if (diameter < 2) return;

        using var outline = new System.Drawing.Drawing2D.GraphicsPath();
        outline.AddArc(0, 0, diameter, diameter, 180, 90);
        outline.AddArc(width - diameter, 0, diameter, diameter, 270, 90);
        outline.AddArc(width - diameter, height - diameter, diameter, diameter, 0, 90);
        outline.AddArc(0, height - diameter, diameter, diameter, 90, 90);
        outline.CloseFigure();
        var oldRegion = bubble.Region;
        bubble.Region = new Region(outline);
        oldRegion?.Dispose();
    }

    private static Label BubbleLabel(string text, int maxWidth, Color color) => new()
    {
        Text = text,
        ForeColor = color,
        AutoSize = true,
        UseMnemonic = false,
        MaximumSize = new Size(maxWidth, 0),
        Margin = new Padding(0, 1, 0, 3)
    };

    private static Bitmap? MakeThumbnail(byte[] data, int maxWidth, int maxHeight)
    {
        try
        {
            using var stream = new MemoryStream(data, writable: false);
            using var image = Image.FromStream(stream);
            if ((long)image.Width * image.Height > 24_000_000) return null;

            double ratio = Math.Min(1d, Math.Min((double)maxWidth / image.Width, (double)maxHeight / image.Height));
            return new Bitmap(image, Math.Max(1, (int)(image.Width * ratio)), Math.Max(1, (int)(image.Height * ratio)));
        }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException
                                   or System.Runtime.InteropServices.ExternalException)
        {
            return null;
        }
    }

    private void ShowImage(Attachment file)
    {
        if (file.Bytes is not { } data) return;

        using var image = MakeThumbnail(data, 1000, 700);
        if (image == null)
        {
            MessageBox.Show(this, "Không xem trước được ảnh này. Bạn vẫn có thể lưu tệp.");
            return;
        }

        using var dialog = new Form
        {
            Text = "Xem ảnh",
            ClientSize = new Size(760, 530),
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Surface
        };
        dialog.Controls.Add(new PictureBox
        {
            Dock = DockStyle.Fill,
            Image = image,
            SizeMode = PictureBoxSizeMode.Zoom
        });
        dialog.ShowDialog(this);
    }

    private Control CreateInlineVideo(Attachment file, int maxWidth)
    {
        int width = Math.Min(360, maxWidth);
        var container = new Panel
        {
            Size = new Size(width, Math.Max(115, width * 9 / 16)),
            BackColor = Canvas
        };
        var view = new WebView2 { Dock = DockStyle.Fill };
        var status = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Đang tải video…",
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Ink
        };
        container.Controls.Add(view);
        container.Controls.Add(status);
        status.BringToFront();

        async void InitializePlayer(object? sender, EventArgs e)
        {
            container.HandleCreated -= InitializePlayer;
            if (file.Bytes is { } data) await LoadInlineVideoAsync(view, status, data);
        }

        container.HandleCreated += InitializePlayer;
        return container;
    }

    private async Task LoadInlineVideoAsync(WebView2 view, Label status, byte[] data)
    {
        DirectoryInfo? folder = null;
        try
        {
            folder = Directory.CreateTempSubdirectory("UniChat-video-");
            var videoPath = Path.Combine(folder.FullName, "clip.mp4");
            var playerPath = Path.Combine(folder.FullName, "player.html");

            await File.WriteAllBytesAsync(videoPath, data);
            await File.WriteAllTextAsync(playerPath, """
                <!doctype html>
                <html lang="vi">
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <meta http-equiv="Content-Security-Policy"
                      content="default-src 'none'; media-src file:; style-src 'unsafe-inline'">
                <title>UniChat Video</title>
                <style>
                    html, body { width: 100%; height: 100%; margin: 0; background: #18191d; }
                    video { display: block; width: 100%; height: 100%; }
                </style>
                <video controls autoplay muted playsinline preload="metadata" src="clip.mp4"></video>
                </html>
                """);

            if (_closing || view.IsDisposed) return;
            await view.EnsureCoreWebView2Async();
            if (_closing || view.IsDisposed) return;

            var playerUri = new Uri(playerPath);
            var core = view.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsWebMessageEnabled = false;
            core.NavigationStarting += (_, args) =>
                args.Cancel = !Uri.TryCreate(args.Uri, UriKind.Absolute, out var target)
                    || !playerUri.Equals(target);
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.NavigationCompleted += (_, args) =>
            {
                if (status.IsDisposed) return;
                if (args.IsSuccess) status.Hide();
                else status.Text = "Không mở được trình phát. Bạn có thể lưu video.";
            };
            core.Navigate(playerUri.AbsoluteUri);
        }
        catch (Exception)
        {
            if (!_closing && !status.IsDisposed)
                status.Text = "Không mở được trình phát. Bạn có thể lưu video.";
        }
        finally
        {
            if (folder is { } playerFolder)
            {
                if (view.IsDisposed) await DeletePlayerFolderAsync(playerFolder);
                else view.Disposed += async (_, _) => await DeletePlayerFolderAsync(playerFolder);
            }
        }
    }

    private static async Task DeletePlayerFolderAsync(DirectoryInfo folder)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                folder.Delete(recursive: true);
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt < 3) await Task.Delay(150);
            }
        }
    }

    private async Task SaveFileAsync(Attachment file)
    {
        if (file.Bytes is not { } data) return;

        using var picker = new SaveFileDialog
        {
            Title = "Lưu tệp",
            FileName = file.Name,
            Filter = "Tất cả tệp|*.*"
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            await File.WriteAllBytesAsync(picker.FileName, data);
            if (!_closing) MessageBox.Show(this, $"Đã lưu {Path.GetFileName(picker.FileName)}.");
        }
        catch (Exception ex)
        {
            if (!_closing) MessageBox.Show(this, "Không lưu được tệp. " + ex.Message);
        }
    }

    private void Disconnected()
    {
        _connected = false;
        _status.Text = "Ngoại tuyến";
        _status.ForeColor = Color.FromArgb(196, 211, 235);
        _knownUsers.Clear();
        _online.Clear();
        _groups.Clear();
        _history.Clear();
        ReleaseFiles();
        _joinStatus.Text = "Kết nối đã ngắt. Bạn có thể vào lại.";
        ShowJoinScreen();
        RefreshRecipients();
    }

    private void ReleaseFiles()
    {
        while (_files.TryDequeue(out var file)) file.Release();
        _fileBytes = 0;
    }

    private void PostToUi(Action action)
    {
        if (_closing || IsDisposed || !IsHandleCreated) return;

        try
        {
            BeginInvoke((Action)(() =>
            {
                if (!_closing && !IsDisposed) action();
            }));
        }
        catch (InvalidOperationException) when (_closing || IsDisposed || !IsHandleCreated)
        {
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.Cancel) return;

        _closing = true;
        _client.Disconnect();
        ReleaseFiles();
    }
}
