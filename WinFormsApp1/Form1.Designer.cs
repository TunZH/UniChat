namespace UniChat.Client
{
    partial class Form1
    {
        private System.ComponentModel.IContainer? components = null;

        private TextBox txtIP = null!;
        private TextBox txtPort = null!;
        private TextBox txtUser = null!;
        private TextBox txtMessage = null!;
        private Button btnConnect = null!;
        private Button btnSend = null!;
        private ListBox lstUsers = null!;
        private RichTextBox rtbChat = null!;
        private Label lblTitle = null!;
        private Label lblIP = null!;
        private Label lblPort = null!;
        private Label lblUser = null!;
        private Label lblChat = null!;
        private Label lblOnline = null!;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                components?.Dispose();
            }

            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            lblTitle = new Label();
            lblIP = new Label();
            lblPort = new Label();
            lblUser = new Label();
            lblChat = new Label();
            lblOnline = new Label();
            txtIP = new TextBox();
            txtPort = new TextBox();
            txtUser = new TextBox();
            txtMessage = new TextBox();
            btnConnect = new Button();
            btnSend = new Button();
            lstUsers = new ListBox();
            rtbChat = new RichTextBox();
            SuspendLayout();

            // Tiêu đề và thông tin kết nối
            lblTitle.AutoSize = true;
            lblTitle.Font = new Font("Segoe UI", 23F, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(29, 78, 216);
            lblTitle.Location = new Point(22, 14);
            lblTitle.Text = "UniChat";

            lblIP.AutoSize = true;
            lblIP.Location = new Point(24, 74);
            lblIP.Text = "IP";

            txtIP.Location = new Point(50, 70);
            txtIP.Size = new Size(130, 27);
            txtIP.Text = "127.0.0.1";

            lblPort.AutoSize = true;
            lblPort.Location = new Point(196, 74);
            lblPort.Text = "Port";

            txtPort.Location = new Point(234, 70);
            txtPort.Size = new Size(68, 27);
            txtPort.Text = "5000";

            lblUser.AutoSize = true;
            lblUser.Location = new Point(318, 74);
            lblUser.Text = "Tên";

            txtUser.Location = new Point(352, 70);
            txtUser.Size = new Size(180, 27);
            txtUser.PlaceholderText = "Nhập tên của bạn";

            btnConnect.Location = new Point(544, 67);
            btnConnect.Size = new Size(112, 33);
            btnConnect.BackColor = Color.FromArgb(219, 234, 254);
            btnConnect.ForeColor = Color.FromArgb(29, 78, 216);
            btnConnect.FlatStyle = FlatStyle.Flat;
            btnConnect.FlatAppearance.BorderSize = 0;
            btnConnect.Text = "Kết nối";
            btnConnect.UseVisualStyleBackColor = false;
            btnConnect.Click += btnConnect_Click;

            // Khung chat
            lblChat.AutoSize = true;
            lblChat.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            lblChat.Location = new Point(24, 119);
            lblChat.Text = "Tin nhắn";

            rtbChat.Anchor = AnchorStyles.Top | AnchorStyles.Bottom |
                             AnchorStyles.Left | AnchorStyles.Right;
            rtbChat.Location = new Point(24, 150);
            rtbChat.Size = new Size(630, 386);
            rtbChat.BackColor = Color.White;
            rtbChat.BorderStyle = BorderStyle.FixedSingle;
            rtbChat.ReadOnly = true;
            rtbChat.TabStop = false;

            // Danh sách người dùng
            lblOnline.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            lblOnline.AutoSize = true;
            lblOnline.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            lblOnline.Location = new Point(674, 119);
            lblOnline.Text = "Người dùng";

            lstUsers.Anchor = AnchorStyles.Top | AnchorStyles.Bottom |
                              AnchorStyles.Right;
            lstUsers.Location = new Point(674, 150);
            lstUsers.Size = new Size(222, 386);
            lstUsers.BackColor = Color.FromArgb(239, 246, 255);
            lstUsers.BorderStyle = BorderStyle.FixedSingle;
            lstUsers.IntegralHeight = false;

            // Ô soạn tin và nút gửi
            txtMessage.Anchor = AnchorStyles.Bottom | AnchorStyles.Left |
                                AnchorStyles.Right;
            txtMessage.Location = new Point(24, 555);
            txtMessage.Size = new Size(526, 27);
            txtMessage.PlaceholderText = "Nhập tin nhắn...";

            btnSend.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnSend.Location = new Point(562, 550);
            btnSend.Size = new Size(92, 36);
            btnSend.BackColor = Color.FromArgb(37, 99, 235);
            btnSend.ForeColor = Color.White;
            btnSend.FlatStyle = FlatStyle.Flat;
            btnSend.FlatAppearance.BorderSize = 0;
            btnSend.Text = "Gửi";
            btnSend.UseVisualStyleBackColor = false;
            btnSend.Click += btnSend_Click;

            // Cửa sổ chính
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(248, 250, 252);
            ForeColor = Color.FromArgb(30, 41, 59);
            Font = new Font("Segoe UI", 10F);
            ClientSize = new Size(920, 610);
            MinimumSize = new Size(720, 460);
            StartPosition = FormStartPosition.CenterScreen;
            Name = "Form1";
            Text = "UniChat";

            Controls.Add(lblTitle);
            Controls.Add(lblIP);
            Controls.Add(txtIP);
            Controls.Add(lblPort);
            Controls.Add(txtPort);
            Controls.Add(lblUser);
            Controls.Add(txtUser);
            Controls.Add(btnConnect);
            Controls.Add(lblChat);
            Controls.Add(rtbChat);
            Controls.Add(lblOnline);
            Controls.Add(lstUsers);
            Controls.Add(txtMessage);
            Controls.Add(btnSend);

            ResumeLayout(false);
            PerformLayout();
        }
    }
}