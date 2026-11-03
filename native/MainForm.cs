using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace DoubleClickChromeTab
{
    internal sealed class MainForm : Form
    {
        private readonly Label statusTitle;
        private readonly Label statusDetail;
        private readonly Panel statusDot;
        private readonly Label compatibilityLabel;
        private readonly CheckBox startupCheckBox;
        private readonly Panel contentViewport;
        private readonly TableLayoutPanel contentLayout;
        private readonly List<Label> sectionBodies = new List<Label>();
        private bool updatingStartupCheckBox;
        private bool allowExit;

        public event EventHandler HiddenToTray;
        public event EventHandler StartupSettingChanged;

        public MainForm()
        {
            Text = "双击关闭 Chrome 标签";
            ClientSize = new Size(960, 608);
            MinimumSize = new Size(760, 620);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(245, 247, 251);
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            ShowInTaskbar = true;

            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
                // Use the system default icon when extraction is unavailable.
            }

            Panel header = CreateHeader();
            Panel statusBar = CreateStatusBar(out statusDot, out statusTitle, out statusDetail, out compatibilityLabel);

            contentViewport = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(245, 247, 251),
                Padding = new Padding(16, 8, 16, 6)
            };

            contentLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.FromArgb(245, 247, 251),
                ColumnCount = 1,
                RowCount = 2,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            contentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            contentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label usageBody;
            Panel usageCard = CreateSection(
                "使用方法",
                "1. 保持本程序运行，程序会一直显示在 Windows 系统托盘。\r\n" +
                "2. 双击 Chrome 顶部的任意标签页，即可关闭该标签。\r\n" +
                "3. 双击标签没有反应时，右键托盘图标并开启“兼容模式”。\r\n" +
                "4. 点击主窗口关闭按钮只会隐藏到托盘，不会退出程序。\r\n" +
                "5. 如需完全退出，请右键托盘图标选择“退出”。\r\n" +
                "6. 双击系统托盘中的本程序图标，可重新打开主窗口。",
                out usageBody);

            Label noticeBody;
            Panel noticeCard = CreateSection(
                "注意事项",
                "1. 仅支持 Chrome 桌面版，网页内容、地址栏和工具栏不会触发。\r\n" +
                "2. 因该程序会模拟 Ctrl+W 按键，用来关闭当前标签页，360 或 Windows 安全中心可能误报为木马。\r\n" +
                "3. 误报处理办法：在相应的误报软件中，添加信任或排除项，详细见 PDF 使用说明。",
                out noticeBody);

            usageCard.Margin = new Padding(0, 0, 0, 8);
            noticeCard.Margin = Padding.Empty;
            contentLayout.Controls.Add(usageCard, 0, 0);
            contentLayout.Controls.Add(noticeCard, 0, 1);
            contentViewport.Controls.Add(contentLayout);

            sectionBodies.Add(usageBody);
            sectionBodies.Add(noticeBody);
            contentViewport.Resize += OnContentViewportResize;
            OnContentViewportResize(contentViewport, EventArgs.Empty);

            Panel footer = CreateFooter(out startupCheckBox);

            Controls.Add(contentViewport);
            Controls.Add(footer);
            Controls.Add(statusBar);
            Controls.Add(header);

            FormClosing += OnFormClosing;
            Shown += (sender, args) => UpdateSectionBodyWidths();
        }

        private Panel CreateHeader()
        {
            Panel header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 120,
                BackColor = Color.FromArgb(37, 99, 235),
                Padding = new Padding(22, 16, 22, 14)
            };

            TableLayoutPanel headerLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64F));
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            headerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 58F));
            headerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 42F));

            PictureBox appIcon = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                Margin = new Padding(0, 4, 16, 4),
                Image = Icon == null ? null : Icon.ToBitmap()
            };

            Label title = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold, GraphicsUnit.Point),
                Text = "双击关闭 Chrome 标签",
                TextAlign = ContentAlignment.BottomLeft,
                AutoSize = false,
                UseCompatibleTextRendering = false
            };

            Label subtitle = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(219, 234, 254),
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
                Text = "双击原生标签页即可关闭  ·  版权归属@阿萌同学  ·  v1.1.8",
                TextAlign = ContentAlignment.TopLeft,
                AutoSize = false,
                Padding = new Padding(0, 6, 0, 0),
                UseCompatibleTextRendering = false
            };

            headerLayout.Controls.Add(appIcon, 0, 0);
            headerLayout.SetRowSpan(appIcon, 2);
            headerLayout.Controls.Add(title, 1, 0);
            headerLayout.Controls.Add(subtitle, 1, 1);
            header.Controls.Add(headerLayout);
            return header;
        }

        private static Panel CreateStatusBar(
            out Panel statusDot,
            out Label statusTitle,
            out Label statusDetail,
            out Label compatibilityLabel)
        {
            Panel statusBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 52,
                BackColor = Color.White,
                Padding = new Padding(22, 6, 22, 6)
            };

            TableLayoutPanel statusLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 18F));
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220F));
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180F));

            statusDot = new Panel
            {
                Size = new Size(10, 10),
                BackColor = Color.FromArgb(22, 163, 74),
                Margin = new Padding(1, 0, 7, 0),
                Anchor = AnchorStyles.Left
            };

            statusTitle = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(22, 32, 51),
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point),
                Text = "正在监听 Chrome 标签页",
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = false,
                AutoEllipsis = true,
                UseCompatibleTextRendering = false
            };

            statusDetail = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(104, 115, 138),
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point),
                Text = "双击 Chrome 标签即可关闭",
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = false,
                AutoEllipsis = true,
                Padding = new Padding(28, 0, 0, 0),
                UseCompatibleTextRendering = false
            };

            compatibilityLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = Color.FromArgb(104, 115, 138),
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point),
                Text = "兼容模式：未开启",
                AutoSize = false,
                AutoEllipsis = true,
                UseCompatibleTextRendering = false,
                Margin = Padding.Empty
            };

            statusLayout.Controls.Add(statusDot, 0, 0);
            statusLayout.Controls.Add(statusTitle, 1, 0);
            statusLayout.Controls.Add(statusDetail, 2, 0);
            statusLayout.Controls.Add(compatibilityLabel, 3, 0);
            statusBar.Controls.Add(statusLayout);
            return statusBar;
        }

        private Panel CreateFooter(out CheckBox startupCheckBox)
        {
            Panel footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 64,
                BackColor = Color.White,
                Padding = new Padding(22, 14, 22, 14)
            };

            TableLayoutPanel footerLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170F));
            footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 136F));
            footerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));

            Label trayTip = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(104, 115, 138),
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point),
                Text = "托盘图标：双击打开主窗口 · 右键查看更多操作",
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                AutoSize = false,
                UseCompatibleTextRendering = false,
                Margin = Padding.Empty
            };

            startupCheckBox = new CheckBox
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                Text = "开机自启动",
                ForeColor = Color.FromArgb(56, 68, 89),
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.System,
                AccessibleName = "开机自启动",
                AccessibleRole = AccessibleRole.CheckButton,
                Margin = Padding.Empty
            };
            startupCheckBox.CheckedChanged += OnStartupCheckBoxChanged;

            Button hideButton = new Button
            {
                Width = 136,
                Height = 36,
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Text = "隐藏到托盘",
                Margin = Padding.Empty,
                Dock = DockStyle.Fill,
                UseCompatibleTextRendering = false
            };
            hideButton.FlatAppearance.BorderSize = 0;
            hideButton.Click += (sender, args) => HideToTray();

            footerLayout.Controls.Add(trayTip, 0, 0);
            footerLayout.Controls.Add(startupCheckBox, 1, 0);
            footerLayout.Controls.Add(hideButton, 2, 0);
            footer.Controls.Add(footerLayout);
            return footer;
        }

        private Panel CreateSection(string title, string text, out Label body)
        {
            Panel section = new Panel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(16, 10, 16, 10),
                Margin = Padding.Empty
            };

            Label heading = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ForeColor = Color.FromArgb(23, 32, 51),
                Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold, GraphicsUnit.Point),
                Text = title,
                TextAlign = ContentAlignment.MiddleLeft,
                UseCompatibleTextRendering = false,
                Margin = Padding.Empty
            };

            body = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ForeColor = Color.FromArgb(56, 68, 89),
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
                Text = text,
                TextAlign = ContentAlignment.TopLeft,
                UseCompatibleTextRendering = false,
                Margin = new Padding(0, 6, 0, 0),
                Padding = Padding.Empty
            };

            section.Controls.Add(body);
            section.Controls.Add(heading);
            Label bodyLabel = body;
            section.SizeChanged += (sender, args) => UpdateBodyWidth(section, bodyLabel);
            return section;
        }

        private void OnContentViewportResize(object sender, EventArgs args)
        {
            int width = contentViewport.ClientSize.Width - contentViewport.Padding.Horizontal - 4;
            if (width > 0)
            {
                contentLayout.Width = width;
            }

            UpdateSectionBodyWidths();
        }

        private void UpdateSectionBodyWidths()
        {
            if (contentViewport == null)
            {
                return;
            }

            int width = contentViewport.ClientSize.Width - contentViewport.Padding.Horizontal - 4;
            if (width < 240)
            {
                width = 240;
            }

            foreach (Label body in sectionBodies)
            {
                Panel section = body.Parent as Panel;
                if (section != null)
                {
                    UpdateBodyWidth(section, body);
                }
                else
                {
                    body.MaximumSize = new Size(width, 0);
                }
            }
        }

        private static void UpdateBodyWidth(Panel section, Label body)
        {
            int width = section.ClientSize.Width - section.Padding.Horizontal - 2;
            if (width < 240)
            {
                width = 240;
            }

            body.MaximumSize = new Size(width, 0);
        }

        public void UpdateStatus(bool enabled, bool compatibilityMode)
        {
            if (enabled)
            {
                statusDot.BackColor = Color.FromArgb(22, 163, 74);
                statusTitle.Text = "正在监听 Chrome 标签页";
                statusDetail.Text = "双击 Chrome 标签即可关闭";
            }
            else
            {
                statusDot.BackColor = Color.FromArgb(245, 158, 11);
                statusTitle.Text = "双击关闭已暂停";
                statusDetail.Text = "请在托盘菜单中勾选“启用双击关闭”";
            }

            compatibilityLabel.Text = compatibilityMode ? "兼容模式：已开启" : "兼容模式：未开启";
        }

        public bool StartupEnabled
        {
            get
            {
                return startupCheckBox.Checked;
            }
            set
            {
                updatingStartupCheckBox = true;
                try
                {
                    startupCheckBox.Checked = value;
                }
                finally
                {
                    updatingStartupCheckBox = false;
                }
            }
        }

        private void OnStartupCheckBoxChanged(object sender, EventArgs args)
        {
            if (updatingStartupCheckBox)
            {
                return;
            }

            EventHandler handler = StartupSettingChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        public void ShowMainWindow()
        {
            if (!Visible)
            {
                Show();
            }

            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }

            ShowInTaskbar = true;
            Activate();
            BringToFront();
        }

        public void PrepareForExit()
        {
            allowExit = true;
        }

        private void HideToTray()
        {
            if (!Visible)
            {
                return;
            }

            Hide();
            EventHandler handler = HiddenToTray;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (allowExit || e.CloseReason == CloseReason.WindowsShutDown)
            {
                return;
            }

            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
            }
        }
    }
}
