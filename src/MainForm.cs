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
        private ComboBox transportACombo;
        private ComboBox transportBCombo;
        private TextBox endpointAInput;
        private TextBox endpointBInput;
        private Label endpointALabel;
        private Label endpointBLabel;
        private ComboBox modeCombo;
        private ComboBox baudCombo;
        private NumericUpDown timeoutInput;
        private ComboBox patternCombo;
        private ComboBox frameLengthCombo;
        private TextBox customPatternInput;
        private NumericUpDown dataSeedInput;
        private ComboBox inFlightWindowCombo;
        private CheckBox randomContentCheck;
        private CheckBox randomFrameLengthCheck;
        private Button startButton;
        private Button stopButton;
        private Button refreshButton;
        private Button clearLogButton;
        private Label statusLabel;
        private RichTextBox logBox;
        private Label aSentValue;
        private Label aOkValue;
        private Label aErrorValue;
        private Label crcErrorValue;
        private Label lostValue;
        private Label duplicateValue;
        private Label outOfOrderValue;
        private Label inFlightValue;
        private Label errorBytesValue;
        private Label errorBitsValue;
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
            MinimumSize = new Size(980, 800);
            Size = new Size(1120, 880);
            BackColor = Background;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Dpi;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
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
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 230F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 225F));
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
            headerSubtitle.Text = "A 连续发送多帧 → B 校验并回传 → A 按序校验与统计";
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

            portAGroup = BuildPortGroup("端点 A · 主动发送端", true, out transportACombo,
                out portACombo, out endpointAInput, out endpointALabel, out portAHint,
                "发送预设数据，并校验接收到的环回内容");
            portBGroup = BuildPortGroup("端点 B · 回传端", false, out transportBCombo,
                out portBCombo, out endpointBInput, out endpointBLabel, out portBHint,
                "接收并校验 A 端数据，无论结果均原样回传");
            layout.Controls.Add(portAGroup, 0, 0);
            layout.Controls.Add(portBGroup, 1, 0);
            layout.Controls.Add(BuildTestConfig(), 2, 0);
            return layout;
        }

        private GroupBox BuildPortGroup(string titleText, bool isEndpointA,
            out ComboBox transportCombo, out ComboBox serialCombo, out TextBox endpointInput,
            out Label endpointLabel, out Label descriptionLabel, string description)
        {
            GroupBox group = CreateGroup(titleText);
            group.Margin = new Padding(0, 0, 12, 12);

            Label typeLabel = CreateFieldLabel("传输类型");
            typeLabel.Location = new Point(18, 32);
            group.Controls.Add(typeLabel);

            transportCombo = new ComboBox();
            transportCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            transportCombo.Items.AddRange(new object[] { "串口", "TCP Client", "TCP Server", "UDP" });
            transportCombo.SelectedIndex = 0;
            transportCombo.Location = new Point(18, 52);
            transportCombo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            transportCombo.Width = group.Width - 36;
            group.Controls.Add(transportCombo);

            endpointLabel = CreateFieldLabel("串口号");
            endpointLabel.Location = new Point(18, 91);
            group.Controls.Add(endpointLabel);

            serialCombo = new ComboBox();
            serialCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            serialCombo.Font = new Font(Font.FontFamily, 10F, FontStyle.Bold);
            serialCombo.Location = new Point(18, 111);
            serialCombo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            serialCombo.Width = group.Width - 36;
            group.Controls.Add(serialCombo);

            endpointInput = new TextBox();
            endpointInput.Font = new Font("Consolas", 9F);
            endpointInput.Location = new Point(18, 111);
            endpointInput.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            endpointInput.Width = group.Width - 36;
            endpointInput.Text = isEndpointA ? "127.0.0.1:9001" : "0.0.0.0:9001";
            endpointInput.Visible = false;
            group.Controls.Add(endpointInput);

            Label serialMode = new Label();
            serialMode.Text = "8N1 无流控 · " + description;
            serialMode.ForeColor = Muted;
            serialMode.AutoEllipsis = true;
            serialMode.Location = new Point(18, 150);
            serialMode.Size = new Size(238, 34);
            serialMode.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            group.Controls.Add(serialMode);
            descriptionLabel = serialMode;
            ComboBox resizingTransport = transportCombo;
            ComboBox resizingSerial = serialCombo;
            TextBox resizingEndpoint = endpointInput;

            group.Resize += delegate
            {
                resizingTransport.Width = Math.Max(80, group.ClientSize.Width - 36);
                resizingSerial.Width = Math.Max(80, group.ClientSize.Width - 36);
                resizingEndpoint.Width = Math.Max(80, group.ClientSize.Width - 36);
                serialMode.Width = Math.Max(80, group.ClientSize.Width - 36);
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

            Label timeoutLabel = CreateFieldLabel("最低超时（ms）");
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

            Label windowLabel = CreateFieldLabel("在途窗口（帧）");
            windowLabel.Location = new Point(300, 35);
            group.Controls.Add(windowLabel);

            inFlightWindowCombo = new ComboBox();
            inFlightWindowCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            inFlightWindowCombo.Items.AddRange(new object[]
            {
                "自动（推荐）", "1", "2", "3", "4", "8", "16", "32"
            });
            inFlightWindowCombo.SelectedIndex = 0;
            inFlightWindowCombo.Location = new Point(300, 59);
            inFlightWindowCombo.Size = new Size(128, 28);
            group.Controls.Add(inFlightWindowCombo);

            Label patternLabel = CreateFieldLabel("预设内容");
            patternLabel.Location = new Point(18, 96);
            group.Controls.Add(patternLabel);

            patternCombo = new ComboBox();
            patternCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            patternCombo.Items.AddRange(new object[]
            {
                "递增 00-FF", "全 55", "全 AA", "55/AA 交替", "自定义 HEX 循环",
                "PRBS7", "PRBS15", "PRBS31"
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
            customPatternInput.Location = new Point(18, 170);
            customPatternInput.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            customPatternInput.Width = Math.Max(120, group.Width - 286);
            customPatternInput.Enabled = false;
            group.Controls.Add(customPatternInput);

            Label customPatternLabel = CreateFieldLabel("自定义 HEX（仅自定义模式）");
            customPatternLabel.Location = new Point(18, 150);
            group.Controls.Add(customPatternLabel);

            Label seedLabel = CreateFieldLabel("数据种子（十进制）");
            seedLabel.Location = new Point(280, 150);
            group.Controls.Add(seedLabel);

            dataSeedInput = new NumericUpDown();
            dataSeedInput.Minimum = 0;
            dataSeedInput.Maximum = uint.MaxValue;
            dataSeedInput.Value = 305419896; // 0x12345678
            dataSeedInput.Location = new Point(280, 170);
            dataSeedInput.Size = new Size(180, 28);
            dataSeedInput.TextAlign = HorizontalAlignment.Left;
            group.Controls.Add(dataSeedInput);
            group.Resize += delegate
            {
                int seedX = Math.Max(230, group.ClientSize.Width - 215);
                seedLabel.Location = new Point(seedX, 150);
                dataSeedInput.Location = new Point(seedX, 170);
                dataSeedInput.Width = Math.Max(100, group.ClientSize.Width - seedX - 18);
                customPatternInput.Width = Math.Max(120, seedX - 30);
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
                "双端点环回（A→B→A）",
                "单端点全双工/自环（A TX↔RX）"
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
            grid.RowCount = 3;
            grid.Margin = new Padding(0, 0, 0, 12);
            for (int index = 0; index < 4; index++)
            {
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            }
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33F));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33F));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 33.34F));

            grid.Controls.Add(CreateStatCard("A 发送帧", out aSentValue, Blue), 0, 0);
            grid.Controls.Add(CreateStatCard("A 校验正确", out aOkValue, Green), 1, 0);
            grid.Controls.Add(CreateStatCard("A 校验错误", out aErrorValue, Red), 2, 0);
            grid.Controls.Add(CreateStatCard("CRC 错误", out crcErrorValue, Red), 3, 0);
            grid.Controls.Add(CreateStatCard("丢帧", out lostValue, Red), 0, 1);
            grid.Controls.Add(CreateStatCard("重复帧", out duplicateValue, Red), 1, 1);
            grid.Controls.Add(CreateStatCard("乱序帧", out outOfOrderValue, Red), 2, 1);
            grid.Controls.Add(CreateStatCard("在途帧（当前 / 窗口）", out inFlightValue, Blue), 3, 1);
            grid.Controls.Add(CreateStatCard("错误字节", out errorBytesValue, Red), 0, 2);
            grid.Controls.Add(CreateStatCard("错误位数", out errorBitsValue, Red), 1, 2);
            grid.Controls.Add(CreateStatCard("运行时间", out elapsedValue, Navy), 2, 2);
            grid.Controls.Add(CreateStatCard("往返耗时（最近 / 平均）", out latencyValue, Navy), 3, 2);
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
            transportACombo.SelectedIndexChanged += delegate { UpdateTransportUi(true); };
            transportBCombo.SelectedIndexChanged += delegate { UpdateTransportUi(false); };
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
                TransportSettings endpointA = BuildTransportSettings(true);
                TransportSettings endpointB = mode == LoopTestMode.DualPortRelay
                    ? BuildTransportSettings(false) : null;
                LoopDataOptions options = BuildDataOptions();
                int baudRate = BaudRateOptions.Parse(baudCombo.Text);
                controller.Start(endpointA, endpointB,
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
            transportACombo.Enabled = !running;
            endpointAInput.Enabled = !running;
            transportBCombo.Enabled = !running;
            endpointBInput.Enabled = !running;
            modeCombo.Enabled = !running;
            baudCombo.Enabled = !running && UsesSerialTransport();
            timeoutInput.Enabled = !running;
            patternCombo.Enabled = !running && !randomContentCheck.Checked;
            frameLengthCombo.Enabled = !running && !randomFrameLengthCheck.Checked;
            customPatternInput.Enabled = !running && !randomContentCheck.Checked &&
                patternCombo.SelectedIndex == (int)PayloadPattern.CustomRepeat;
            randomContentCheck.Enabled = !running;
            randomFrameLengthCheck.Enabled = !running;
            dataSeedInput.Enabled = !running;
            inFlightWindowCombo.Enabled = !running;
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
            portAGroup.Text = singlePort ? "端点 A · 全双工测试端点" : "端点 A · 主动发送端";
            portBGroup.Text = singlePort ? "端点 B · 单端模式不使用" : "端点 B · 回传端";
            portBGroup.Enabled = !singlePort;
            transportBCombo.Enabled = !running && !singlePort;
            portBCombo.Enabled = !running && !singlePort &&
                (TransportKind)transportBCombo.SelectedIndex == TransportKind.Serial;
            endpointBInput.Enabled = !running && !singlePort &&
                (TransportKind)transportBCombo.SelectedIndex != TransportKind.Serial;
            wiringHint.Text = singlePort
                ? "串口可 TX↔RX 自环；网络端点需由对端回显同一协议帧"
                : "A/B 可分别选择串口、TCP Client、TCP Server 或 UDP";
            headerSubtitle.Text = singlePort
                ? "A TX 连续发送多帧 → 同端 RX 独立接收、同步并校验"
                : "A 连续发送多帧 → B 校验并回传 → A 按序校验与统计";
            SetStatTitle(latencyValue, singlePort
                ? "自环耗时（最近 / 平均）" : "往返耗时（最近 / 平均）");
            SetStatTitle(crcErrorValue, singlePort ? "CRC 错误" : "CRC 错误（A+B 检出）");
            UpdateTransportUi(true);
            UpdateTransportUi(false);
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
                RandomFrameLength = randomFrameLengthCheck.Checked,
                DataSeed = decimal.ToUInt32(dataSeedInput.Value),
                InFlightWindow = inFlightWindowCombo.SelectedIndex == 0 ? 0 :
                    int.Parse(inFlightWindowCombo.SelectedItem.ToString())
            };

            if (!options.RandomContent && options.Pattern == PayloadPattern.CustomRepeat)
            {
                options.CustomPattern = PayloadCodec.Parse(customPatternInput.Text, true);
            }
            options.Validate();
            return options;
        }

        private TransportSettings BuildTransportSettings(bool isEndpointA)
        {
            ComboBox transportCombo = isEndpointA ? transportACombo : transportBCombo;
            ComboBox serialCombo = isEndpointA ? portACombo : portBCombo;
            TextBox endpointInput = isEndpointA ? endpointAInput : endpointBInput;
            TransportKind kind = (TransportKind)transportCombo.SelectedIndex;
            string serialPort = serialCombo.SelectedItem == null
                ? null : serialCombo.SelectedItem.ToString();
            return TransportSettings.Parse(kind, serialPort, endpointInput.Text);
        }

        private void UpdateTransportUi(bool isEndpointA)
        {
            ComboBox transportCombo = isEndpointA ? transportACombo : transportBCombo;
            ComboBox serialCombo = isEndpointA ? portACombo : portBCombo;
            TextBox endpointInput = isEndpointA ? endpointAInput : endpointBInput;
            Label endpointLabel = isEndpointA ? endpointALabel : endpointBLabel;
            Label hint = isEndpointA ? portAHint : portBHint;
            if (transportCombo == null || transportCombo.SelectedIndex < 0) return;

            TransportKind kind = (TransportKind)transportCombo.SelectedIndex;
            bool serial = kind == TransportKind.Serial;
            bool running = controller.GetSnapshot().IsRunning;
            bool endpointEnabled = !running &&
                (isEndpointA || GetSelectedMode() == LoopTestMode.DualPortRelay);
            serialCombo.Visible = serial;
            serialCombo.Enabled = endpointEnabled && serial;
            endpointInput.Visible = !serial;
            endpointInput.Enabled = endpointEnabled && !serial;

            if (!serial)
            {
                TransportKind? previousKind = endpointInput.Tag is TransportKind
                    ? (TransportKind?)endpointInput.Tag : null;
                string previousDefault = previousKind.HasValue
                    ? GetDefaultEndpoint(isEndpointA, previousKind.Value) : null;
                if (!previousKind.HasValue || string.IsNullOrWhiteSpace(endpointInput.Text) ||
                    string.Equals(endpointInput.Text, previousDefault, StringComparison.Ordinal))
                    endpointInput.Text = GetDefaultEndpoint(isEndpointA, kind);
                endpointInput.Tag = kind;
            }

            switch (kind)
            {
                case TransportKind.TcpClient:
                    endpointLabel.Text = "服务器地址（主机:端口）";
                    hint.Text = "主动连接 TCP Server；接收与发送在线程中独立进行";
                    break;
                case TransportKind.TcpServer:
                    endpointLabel.Text = "监听地址（本机地址:端口）";
                    hint.Text = "启动后持续监听；未连接前不会发送测试帧";
                    break;
                case TransportKind.Udp:
                    endpointLabel.Text = "UDP（本地端口@远端主机:端口）";
                    hint.Text = "示例 9000@127.0.0.1:9001；每个端点本地端口需不同";
                    break;
                default:
                    endpointLabel.Text = "串口号";
                    hint.Text = "8N1 无流控 · " + (isEndpointA
                        ? "发送并校验环回内容" : "校验后原样回传");
                    break;
            }
            if (baudCombo != null) baudCombo.Enabled = !running && UsesSerialTransport();
        }

        private bool UsesSerialTransport()
        {
            if (transportACombo == null || transportACombo.SelectedIndex < 0) return true;
            if ((TransportKind)transportACombo.SelectedIndex == TransportKind.Serial) return true;
            return GetSelectedMode() == LoopTestMode.DualPortRelay && transportBCombo != null &&
                transportBCombo.SelectedIndex >= 0 &&
                (TransportKind)transportBCombo.SelectedIndex == TransportKind.Serial;
        }

        private static string GetDefaultEndpoint(bool isEndpointA, TransportKind kind)
        {
            switch (kind)
            {
                case TransportKind.TcpClient:
                    return isEndpointA ? "127.0.0.1:9001" : "127.0.0.1:9000";
                case TransportKind.TcpServer:
                    return isEndpointA ? "0.0.0.0:9000" : "0.0.0.0:9001";
                case TransportKind.Udp:
                    return isEndpointA
                        ? "9000@127.0.0.1:9001" : "9001@127.0.0.1:9000";
                default:
                    return string.Empty;
            }
        }

        private void UpdateDataOptionState()
        {
            bool settingsEnabled = !controller.GetSnapshot().IsRunning;
            patternCombo.Enabled = settingsEnabled && !randomContentCheck.Checked;
            frameLengthCombo.Enabled = settingsEnabled && !randomFrameLengthCheck.Checked;
            dataSeedInput.Enabled = settingsEnabled;
            customPatternInput.Enabled = settingsEnabled && !randomContentCheck.Checked &&
                patternCombo.SelectedIndex == (int)PayloadPattern.CustomRepeat;
        }

        private void UpdateStatistics()
        {
            LoopSnapshot snapshot = controller.GetSnapshot();
            aSentValue.Text = snapshot.ASent.ToString("N0");
            aOkValue.Text = snapshot.AReceivedOk.ToString("N0");
            aErrorValue.Text = snapshot.AReceivedError.ToString("N0");
            crcErrorValue.Text = snapshot.CrcErrors.ToString("N0");
            lostValue.Text = snapshot.LostFrames.ToString("N0");
            duplicateValue.Text = snapshot.DuplicateFrames.ToString("N0");
            outOfOrderValue.Text = snapshot.OutOfOrderFrames.ToString("N0");
            inFlightValue.Text = snapshot.InFlightFrames.ToString("N0") + " / " +
                snapshot.WindowSize.ToString("N0");
            errorBytesValue.Text = snapshot.ErrorBytes.ToString("N0");
            errorBitsValue.Text = snapshot.ErrorBits.ToString("N0");
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
