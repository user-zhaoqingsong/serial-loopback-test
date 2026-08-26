using System;
using System.Drawing;
using System.IO.Ports;
using System.Linq;
using System.Windows.Forms;

namespace RsLoopTest
{
    internal sealed class MainForm : Form
    {
        private static readonly Color Navy = Color.FromArgb(25, 49, 83);
        private static readonly Color Blue = Color.FromArgb(37, 99, 235);
        private static readonly Color Green = Color.FromArgb(22, 163, 74);
        private static readonly Color Red = Color.FromArgb(220, 38, 38);
        private static readonly Color Muted = Color.FromArgb(100, 116, 139);
        private static readonly Color Surface = Color.White;
        private static readonly Color Background = Color.FromArgb(241, 245, 249);

        private readonly SerialLoopController controller = new SerialLoopController();
        private readonly Timer displayTimer = new Timer();
        private ComboBox portACombo;
        private ComboBox portBCombo;
        private ComboBox modeCombo;
        private ComboBox baudCombo;
        private NumericUpDown timeoutInput;
        private ComboBox patternCombo;
        private ComboBox frameLengthCombo;
        private TextBox customPatternInput;
        private CheckBox randomContentCheck;
        private CheckBox randomFrameLengthCheck;
        private Button startButton;
        private Button stopButton;
        private Button refreshButton;
        private Button clearLogButton;
        private Label statusLabel;
        private RichTextBox logBox;
        private Label aSentValue;
        private Label bOkValue;
        private Label bErrorValue;
        private Label bSentValue;
        private Label aOkValue;
        private Label aErrorValue;
        private Label elapsedValue;
        private Label latencyValue;
        private GroupBox portAGroup;
        private GroupBox portBGroup;
        private Label wiringHint;
        private Label headerSubtitle;
        private Label portAHint;
        private Label portBHint;
        private bool closing;

        public MainForm()
        {
            InitializeWindow();
            BuildInterface();
            HookEvents();
            RefreshPorts();
            UpdateModeUi();
            displayTimer.Interval = 200;
            displayTimer.Start();
            AppendLog("程序已就绪。请选择测试模式和串口。", false);
        }

        private void InitializeWindow()
        {
            Text = "串口环回测试";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(980, 720);
            Size = new Size(1120, 800);
            BackColor = Background;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Dpi;
        }

        private void BuildInterface()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(18);
            root.BackColor = Background;
            root.ColumnCount = 1;
            root.RowCount = 5;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 202F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 172F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            Controls.Add(root);

            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(BuildConfiguration(), 0, 1);
            root.Controls.Add(BuildActions(), 0, 2);
            root.Controls.Add(BuildStatistics(), 0, 3);
            root.Controls.Add(BuildLogArea(), 0, 4);
        }

        private Control BuildHeader()
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.BackColor = Navy;
            panel.Padding = new Padding(22, 14, 22, 12);
            panel.Margin = new Padding(0, 0, 0, 12);

            Label title = new Label();
            title.Text = "串口环回测试";
            title.ForeColor = Color.White;
            title.Font = new Font(Font.FontFamily, 18F, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(20, 12);
            panel.Controls.Add(title);

            headerSubtitle = new Label();
            headerSubtitle.Text = "A 发送 → B 校验并回传 → A 校验 → 自动进入下一轮";
            headerSubtitle.ForeColor = Color.FromArgb(203, 213, 225);
            headerSubtitle.Font = new Font(Font.FontFamily, 9.5F);
            headerSubtitle.AutoSize = true;
            headerSubtitle.Location = new Point(22, 47);
            panel.Controls.Add(headerSubtitle);

            statusLabel = new Label();
            statusLabel.Text = "●  未运行";
            statusLabel.TextAlign = ContentAlignment.MiddleCenter;
            statusLabel.BackColor = Color.FromArgb(51, 65, 85);
            statusLabel.ForeColor = Color.FromArgb(203, 213, 225);
            statusLabel.Font = new Font(Font.FontFamily, 10F, FontStyle.Bold);
            statusLabel.Size = new Size(126, 36);
            statusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            panel.Controls.Add(statusLabel);
            panel.Resize += delegate { statusLabel.Location = new Point(panel.ClientSize.Width - 148, 18); };
            return panel;
        }

        private Control BuildConfiguration()
        {
            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 3;
            layout.RowCount = 1;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48F));
            layout.Margin = new Padding(0);

            portAGroup = BuildPortGroup("端口 A · 主动发送端", out portACombo, out portAHint,
                "发送预设数据，并校验接收到的环回内容");
            portBGroup = BuildPortGroup("端口 B · 回传端", out portBCombo, out portBHint,
                "接收并校验 A 端数据，无论结果均原样回传");
            layout.Controls.Add(portAGroup, 0, 0);
            layout.Controls.Add(portBGroup, 1, 0);
            layout.Controls.Add(BuildTestConfig(), 2, 0);
            return layout;
        }

        private GroupBox BuildPortGroup(string titleText, out ComboBox combo,
            out Label descriptionLabel, string description)
        {
            GroupBox group = CreateGroup(titleText);
            group.Margin = new Padding(0, 0, 12, 12);

            Label portLabel = CreateFieldLabel("串口号");
            portLabel.Location = new Point(18, 36);
            group.Controls.Add(portLabel);

            combo = new ComboBox();
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.Font = new Font(Font.FontFamily, 11F, FontStyle.Bold);
            combo.Location = new Point(18, 61);
            combo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            combo.Width = group.Width - 36;
            group.Controls.Add(combo);
            ComboBox resizingCombo = combo;

            Label serialMode = new Label();
            serialMode.Text = "8 数据位  ·  无校验  ·  1 停止位";
            serialMode.ForeColor = Navy;
            serialMode.AutoSize = true;
            serialMode.Location = new Point(18, 104);
            group.Controls.Add(serialMode);

            Label hint = new Label();
            hint.Text = description;
            hint.ForeColor = Muted;
            hint.AutoEllipsis = true;
            hint.Location = new Point(18, 135);
            hint.Size = new Size(238, 36);
            hint.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            group.Controls.Add(hint);
            descriptionLabel = hint;

            group.Resize += delegate
            {
                resizingCombo.Width = Math.Max(80, group.ClientSize.Width - 36);
                hint.Width = Math.Max(80, group.ClientSize.Width - 36);
            };
            return group;
        }

        private Control BuildTestConfig()
        {
            GroupBox group = CreateGroup("测试参数 · 两端共用同一数据规则");
            group.Margin = new Padding(0, 0, 0, 12);

            Label baudLabel = CreateFieldLabel("波特率");
            baudLabel.Location = new Point(18, 35);
            group.Controls.Add(baudLabel);

            baudCombo = new ComboBox();
            baudCombo.DropDownStyle = ComboBoxStyle.DropDown;
            baudCombo.MaxDropDownItems = 12;
            baudCombo.MaxLength = 7;
            foreach (int commonRate in BaudRateOptions.CommonRates)
            {
                baudCombo.Items.Add(commonRate.ToString());
            }
            baudCombo.Text = "115200";
            baudCombo.Location = new Point(18, 59);
            baudCombo.Size = new Size(125, 28);
            group.Controls.Add(baudCombo);

            Label timeoutLabel = CreateFieldLabel("最低超时 (ms，低速自动延长)");
            timeoutLabel.Location = new Point(160, 35);
            group.Controls.Add(timeoutLabel);

            timeoutInput = new NumericUpDown();
            timeoutInput.Minimum = 100;
            timeoutInput.Maximum = 60000;
            timeoutInput.Increment = 100;
            timeoutInput.Value = 2000;
            timeoutInput.Location = new Point(160, 59);
            timeoutInput.Size = new Size(125, 28);
            group.Controls.Add(timeoutInput);

            Label patternLabel = CreateFieldLabel("预设内容");
            patternLabel.Location = new Point(18, 96);
            group.Controls.Add(patternLabel);

            patternCombo = new ComboBox();
            patternCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            patternCombo.Items.AddRange(new object[]
            {
                "递增 00-FF", "全 55", "全 AA", "55/AA 交替", "自定义 HEX 循环"
            });
            patternCombo.SelectedIndex = 0;
            patternCombo.Location = new Point(18, 119);
            patternCombo.Size = new Size(142, 28);
            group.Controls.Add(patternCombo);

            Label lengthLabel = CreateFieldLabel("帧长（字节）");
            lengthLabel.Location = new Point(174, 96);
            group.Controls.Add(lengthLabel);

            frameLengthCombo = new ComboBox();
            frameLengthCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            frameLengthCombo.Items.AddRange(new object[] { "20", "40", "60", "80", "100" });
            frameLengthCombo.SelectedItem = "20";
            frameLengthCombo.Location = new Point(174, 119);
            frameLengthCombo.Size = new Size(82, 28);
            group.Controls.Add(frameLengthCombo);

            randomContentCheck = new CheckBox();
            randomContentCheck.Text = "内容随机";
            randomContentCheck.ForeColor = Navy;
            randomContentCheck.AutoSize = true;
            randomContentCheck.Location = new Point(270, 121);
            group.Controls.Add(randomContentCheck);

            randomFrameLengthCheck = new CheckBox();
            randomFrameLengthCheck.Text = "帧长随机";
            randomFrameLengthCheck.ForeColor = Navy;
            randomFrameLengthCheck.AutoSize = true;
            randomFrameLengthCheck.Location = new Point(360, 121);
            group.Controls.Add(randomFrameLengthCheck);

            customPatternInput = new TextBox();
            customPatternInput.Text = "55 AA 00 FF";
            customPatternInput.Font = new Font("Consolas", 9F);
            customPatternInput.Location = new Point(18, 153);
            customPatternInput.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            customPatternInput.Width = group.Width - 36;
            customPatternInput.Enabled = false;
            group.Controls.Add(customPatternInput);
            group.Resize += delegate
            {
                customPatternInput.Width = Math.Max(120, group.ClientSize.Width - 36);
            };
            return group;
        }

        private Control BuildActions()
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.Margin = new Padding(0);

            refreshButton = CreateButton("刷新串口", Color.White, Navy, Color.FromArgb(203, 213, 225));
            refreshButton.Location = new Point(0, 4);
            panel.Controls.Add(refreshButton);

            startButton = CreateButton("开始环回测试", Blue, Color.White, Blue);
            startButton.Size = new Size(156, 40);
            startButton.Location = new Point(118, 4);
            panel.Controls.Add(startButton);

            stopButton = CreateButton("停止", Color.White, Red, Color.FromArgb(254, 202, 202));
            stopButton.Location = new Point(282, 4);
            stopButton.Enabled = false;
            panel.Controls.Add(stopButton);

            Label modeLabel = new Label();
            modeLabel.Text = "测试模式";
            modeLabel.ForeColor = Muted;
            modeLabel.AutoSize = true;
            modeLabel.Location = new Point(410, 15);
            panel.Controls.Add(modeLabel);

            modeCombo = new ComboBox();
            modeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            modeCombo.Items.AddRange(new object[]
            {
                "双端口环回（A→B→A）",
                "单端口全双工自环（A TX↔RX）"
            });
            modeCombo.SelectedIndex = 0;
            modeCombo.Location = new Point(470, 10);
            modeCombo.Size = new Size(230, 28);
            panel.Controls.Add(modeCombo);

            wiringHint = new Label();
            wiringHint.Text = "A/B 的 TX、RX 交叉连接，并连接 GND";
            wiringHint.ForeColor = Muted;
            wiringHint.TextAlign = ContentAlignment.MiddleRight;
            wiringHint.AutoEllipsis = true;
            wiringHint.Location = new Point(712, 4);
            wiringHint.Size = new Size(Math.Max(80, panel.Width - 712), 40);
            wiringHint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            panel.Controls.Add(wiringHint);
            panel.Resize += delegate
            {
                wiringHint.Size = new Size(Math.Max(80, panel.ClientSize.Width - 712), 40);
            };
            return panel;
        }

        private Control BuildStatistics()
        {
            TableLayoutPanel grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.ColumnCount = 4;
            grid.RowCount = 2;
            grid.Margin = new Padding(0, 0, 0, 12);
            for (int index = 0; index < 4; index++)
            {
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            }
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

            grid.Controls.Add(CreateStatCard("A 发送帧", out aSentValue, Blue), 0, 0);
            grid.Controls.Add(CreateStatCard("B 校验正确", out bOkValue, Green), 1, 0);
            grid.Controls.Add(CreateStatCard("B 校验错误", out bErrorValue, Red), 2, 0);
            grid.Controls.Add(CreateStatCard("B 回传帧", out bSentValue, Blue), 3, 0);
            grid.Controls.Add(CreateStatCard("A 校验正确", out aOkValue, Green), 0, 1);
            grid.Controls.Add(CreateStatCard("A 校验错误", out aErrorValue, Red), 1, 1);
            grid.Controls.Add(CreateStatCard("运行时间", out elapsedValue, Navy), 2, 1);
            grid.Controls.Add(CreateStatCard("往返耗时（最近 / 平均）", out latencyValue, Navy), 3, 1);
            return grid;
        }

        private Control BuildLogArea()
        {
            GroupBox group = CreateGroup("运行日志");
            group.Margin = new Padding(0);

            logBox = new RichTextBox();
            logBox.Location = new Point(12, 38);
            logBox.Size = new Size(Math.Max(100, group.Width - 24), Math.Max(50, group.Height - 50));
            logBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            logBox.ReadOnly = true;
            logBox.BackColor = Color.FromArgb(248, 250, 252);
            logBox.BorderStyle = BorderStyle.None;
            logBox.Font = new Font("Consolas", 9F);
            logBox.DetectUrls = false;
            group.Controls.Add(logBox);

            clearLogButton = CreateButton("清空日志", Color.White, Navy, Color.FromArgb(203, 213, 225));
            clearLogButton.Size = new Size(82, 27);
            clearLogButton.Location = new Point(Math.Max(12, group.Width - 94), 8);
            clearLogButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            group.Controls.Add(clearLogButton);
            clearLogButton.BringToFront();
            return group;
        }

        private GroupBox CreateGroup(string text)
        {
            GroupBox group = new GroupBox();
            group.Text = text;
            group.Dock = DockStyle.Fill;
            group.BackColor = Surface;
            group.ForeColor = Navy;
            group.Font = new Font(Font.FontFamily, 9F, FontStyle.Bold);
            group.Padding = new Padding(12);
            return group;
        }

        private static Label CreateFieldLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.ForeColor = Muted;
            label.AutoSize = true;
            return label;
        }

        private Button CreateButton(string text, Color backColor, Color foreColor, Color borderColor)
        {
            Button button = new Button();
            button.Text = text;
            button.Size = new Size(110, 40);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = borderColor;
            button.FlatAppearance.BorderSize = 1;
            button.BackColor = backColor;
            button.ForeColor = foreColor;
            button.Font = new Font(Font.FontFamily, 9F, FontStyle.Bold);
            button.Cursor = Cursors.Hand;
            return button;
        }

        private Control CreateStatCard(string title, out Label valueLabel, Color accent)
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.Margin = new Padding(0, 0, 10, 8);
            panel.BackColor = Surface;

            Panel bar = new Panel();
            bar.Dock = DockStyle.Left;
            bar.Width = 4;
            bar.BackColor = accent;
            panel.Controls.Add(bar);

            Label titleLabel = new Label();
            titleLabel.Text = title;
            titleLabel.ForeColor = Muted;
            titleLabel.AutoSize = true;
            titleLabel.Location = new Point(16, 11);
            panel.Controls.Add(titleLabel);

            valueLabel = new Label();
            valueLabel.Text = "0";
            valueLabel.ForeColor = Navy;
            valueLabel.Font = new Font(Font.FontFamily, 14F, FontStyle.Bold);
            valueLabel.AutoSize = true;
            valueLabel.Location = new Point(14, 35);
            valueLabel.Tag = titleLabel;
            panel.Controls.Add(valueLabel);
            return panel;
        }

        private void HookEvents()
        {
            refreshButton.Click += delegate { RefreshPorts(); };
            startButton.Click += StartButtonClick;
            stopButton.Click += delegate { controller.Stop("用户停止"); };
            clearLogButton.Click += delegate { logBox.Clear(); };
            patternCombo.SelectedIndexChanged += delegate { UpdateDataOptionState(); };
            randomContentCheck.CheckedChanged += delegate { UpdateDataOptionState(); };
            randomFrameLengthCheck.CheckedChanged += delegate { UpdateDataOptionState(); };
            modeCombo.SelectedIndexChanged += delegate { UpdateModeUi(); };
            displayTimer.Tick += delegate { UpdateStatistics(); };
            controller.LogAvailable += ControllerLogAvailable;
            controller.TestStopped += ControllerTestStopped;
            FormClosing += MainFormClosing;
        }

        private void RefreshPorts()
        {
            string oldA = portACombo.SelectedItem as string;
            string oldB = portBCombo.SelectedItem as string;
            string[] ports = SerialPort.GetPortNames()
                .OrderBy(delegate(string name) { return PortSortKey(name); })
                .ToArray();

            portACombo.Items.Clear();
            portBCombo.Items.Clear();
            portACombo.Items.AddRange(ports);
            portBCombo.Items.AddRange(ports);

            SelectPort(portACombo, oldA, ports.Length > 0 ? 0 : -1);
            SelectPort(portBCombo, oldB, ports.Length > 1 ? 1 : (ports.Length > 0 ? 0 : -1));
            AppendLog("已刷新串口，共发现 " + ports.Length + " 个端口。", false);
        }

        private static int PortSortKey(string portName)
        {
            int value;
            return int.TryParse(portName.Replace("COM", string.Empty), out value) ? value : int.MaxValue;
        }

        private static void SelectPort(ComboBox combo, string oldValue, int fallbackIndex)
        {
            if (!string.IsNullOrEmpty(oldValue) && combo.Items.Contains(oldValue))
            {
                combo.SelectedItem = oldValue;
            }
            else if (fallbackIndex >= 0 && fallbackIndex < combo.Items.Count)
            {
                combo.SelectedIndex = fallbackIndex;
            }
        }

        private void StartButtonClick(object sender, EventArgs eventArgs)
        {
            try
            {
                LoopTestMode mode = GetSelectedMode();
                if (portACombo.SelectedItem == null)
                {
                    throw new InvalidOperationException("未检测到端口 A，请连接设备后点击“刷新串口”。");
                }
                if (mode == LoopTestMode.DualPortRelay && portBCombo.SelectedItem == null)
                {
                    throw new InvalidOperationException("双端口模式需要选择端口 B。");
                }

                LoopDataOptions options = BuildDataOptions();
                int baudRate = BaudRateOptions.Parse(baudCombo.Text);
                string portBName = portBCombo.SelectedItem == null
                    ? null : portBCombo.SelectedItem.ToString();
                controller.Start(portACombo.SelectedItem.ToString(), portBName,
                    baudRate, options, decimal.ToInt32(timeoutInput.Value), mode);
                SetRunningState(true);
            }
            catch (Exception exception)
            {
                AppendLog("启动失败：" + exception.Message, true);
                MessageBox.Show(this, exception.Message, "无法启动测试",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void SetRunningState(bool running)
        {
            portACombo.Enabled = !running;
            modeCombo.Enabled = !running;
            baudCombo.Enabled = !running;
            timeoutInput.Enabled = !running;
            patternCombo.Enabled = !running && !randomContentCheck.Checked;
            frameLengthCombo.Enabled = !running && !randomFrameLengthCheck.Checked;
            customPatternInput.Enabled = !running && !randomContentCheck.Checked &&
                patternCombo.SelectedIndex == (int)PayloadPattern.CustomRepeat;
            randomContentCheck.Enabled = !running;
            randomFrameLengthCheck.Enabled = !running;
            refreshButton.Enabled = !running;
            startButton.Enabled = !running;
            stopButton.Enabled = running;

            statusLabel.Text = running ? "●  运行中" : "●  未运行";
            statusLabel.ForeColor = running ? Color.FromArgb(187, 247, 208) : Color.FromArgb(203, 213, 225);
            statusLabel.BackColor = running ? Color.FromArgb(20, 83, 45) : Color.FromArgb(51, 65, 85);
            UpdateModeUi();
        }

        private LoopTestMode GetSelectedMode()
        {
            return modeCombo.SelectedIndex == 1
                ? LoopTestMode.SinglePortFullDuplex : LoopTestMode.DualPortRelay;
        }

        private void UpdateModeUi()
        {
            if (modeCombo == null || portBGroup == null)
            {
                return;
            }

            bool running = controller.GetSnapshot().IsRunning;
            bool singlePort = GetSelectedMode() == LoopTestMode.SinglePortFullDuplex;
            portAGroup.Text = singlePort ? "端口 A · 全双工自环端口" : "端口 A · 主动发送端";
            portBGroup.Text = singlePort ? "端口 B · 单端模式不使用" : "端口 B · 回传端";
            portBGroup.Enabled = !singlePort;
            portBCombo.Enabled = !running && !singlePort;
            wiringHint.Text = singlePort
                ? "单端：A 的 TX 与 RX 短接（差分口同极性相连）"
                : "双端：A/B 的 TX、RX 交叉连接，并连接 GND";
            headerSubtitle.Text = singlePort
                ? "A 发送 → 同端 RX 接收并校验 → 自动进入下一轮"
                : "A 发送 → B 校验并回传 → A 校验 → 自动进入下一轮";
            portAHint.Text = singlePort
                ? "发送数据，并从同一端口 RX 接收校验"
                : "发送预设数据，并校验 B 端回传内容";
            portBHint.Text = singlePort
                ? "此模式只打开端口 A，不占用端口 B"
                : "接收并校验 A 端数据，无论结果均原样回传";

            SetStatTitle(bOkValue, singlePort ? "A 接收帧" : "B 校验正确");
            SetStatTitle(bErrorValue, singlePort ? "A 接收字节" : "B 校验错误");
            SetStatTitle(bSentValue, singlePort ? "环回接线" : "B 回传帧");
            SetStatTitle(latencyValue, singlePort
                ? "自环耗时（最近 / 平均）" : "往返耗时（最近 / 平均）");
            UpdateStatistics();
        }

        private static void SetStatTitle(Label valueLabel, string text)
        {
            Label titleLabel = valueLabel == null ? null : valueLabel.Tag as Label;
            if (titleLabel != null)
            {
                titleLabel.Text = text;
            }
        }

        private LoopDataOptions BuildDataOptions()
        {
            LoopDataOptions options = new LoopDataOptions
            {
                Pattern = (PayloadPattern)patternCombo.SelectedIndex,
                FrameLength = int.Parse(frameLengthCombo.SelectedItem.ToString()),
                RandomContent = randomContentCheck.Checked,
                RandomFrameLength = randomFrameLengthCheck.Checked
            };

            if (!options.RandomContent && options.Pattern == PayloadPattern.CustomRepeat)
            {
                options.CustomPattern = PayloadCodec.Parse(customPatternInput.Text, true);
            }
            options.Validate();
            return options;
        }

        private void UpdateDataOptionState()
        {
            bool settingsEnabled = !controller.GetSnapshot().IsRunning;
            patternCombo.Enabled = settingsEnabled && !randomContentCheck.Checked;
            frameLengthCombo.Enabled = settingsEnabled && !randomFrameLengthCheck.Checked;
            customPatternInput.Enabled = settingsEnabled && !randomContentCheck.Checked &&
                patternCombo.SelectedIndex == (int)PayloadPattern.CustomRepeat;
        }

        private void UpdateStatistics()
        {
            LoopSnapshot snapshot = controller.GetSnapshot();
            bool singlePort = GetSelectedMode() == LoopTestMode.SinglePortFullDuplex;
            aSentValue.Text = snapshot.ASent.ToString("N0");
            bOkValue.Text = singlePort
                ? (snapshot.AReceivedOk + snapshot.AReceivedError).ToString("N0")
                : snapshot.BReceivedOk.ToString("N0");
            bErrorValue.Text = singlePort
                ? snapshot.TotalBytes.ToString("N0") : snapshot.BReceivedError.ToString("N0");
            bSentValue.Text = singlePort ? "TX↔RX" : snapshot.BSent.ToString("N0");
            aOkValue.Text = snapshot.AReceivedOk.ToString("N0");
            aErrorValue.Text = snapshot.AReceivedError.ToString("N0");
            elapsedValue.Text = string.Format("{0:00}:{1:00}:{2:00}",
                (int)snapshot.Elapsed.TotalHours, snapshot.Elapsed.Minutes, snapshot.Elapsed.Seconds);
            latencyValue.Text = string.Format("{0:0.0} / {1:0.0} ms",
                snapshot.LastRoundTripMilliseconds, snapshot.AverageRoundTripMilliseconds);
        }

        private void ControllerLogAvailable(string message, bool isError)
        {
            if (IsDisposed || closing)
            {
                return;
            }
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string, bool>(ControllerLogAvailable), message, isError);
                return;
            }
            AppendLog(message, isError);
        }

        private void ControllerTestStopped(string reason)
        {
            if (IsDisposed || closing)
            {
                return;
            }
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(ControllerTestStopped), reason);
                return;
            }

            SetRunningState(false);
            UpdateStatistics();
            if (!string.Equals(reason, "用户停止", StringComparison.Ordinal))
            {
                MessageBox.Show(this, reason, "环回测试已停止",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void AppendLog(string message, bool isError)
        {
            if (logBox == null)
            {
                return;
            }
            logBox.SelectionStart = logBox.TextLength;
            logBox.SelectionLength = 0;
            logBox.SelectionColor = isError ? Red : Color.FromArgb(51, 65, 85);
            logBox.AppendText("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + message + Environment.NewLine);
            logBox.SelectionColor = logBox.ForeColor;
            logBox.ScrollToCaret();
        }

        private void MainFormClosing(object sender, FormClosingEventArgs eventArgs)
        {
            closing = true;
            displayTimer.Stop();
            controller.LogAvailable -= ControllerLogAvailable;
            controller.TestStopped -= ControllerTestStopped;
            controller.Dispose();
        }
    }
}
