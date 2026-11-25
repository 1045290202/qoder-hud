using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using Ellipse = System.Windows.Shapes.Ellipse;
using Shape = System.Windows.Shapes.Shape;

namespace QoderHud
{
    sealed class DarkMenuColorTable : Forms.ProfessionalColorTable
    {
        static readonly Drawing.Color Bg = Drawing.Color.FromArgb(255, 21, 28, 41);
        static readonly Drawing.Color Hover = Drawing.Color.FromArgb(255, 38, 51, 74);
        static readonly Drawing.Color Line = Drawing.Color.FromArgb(255, 48, 62, 86);

        public override Drawing.Color ToolStripDropDownBackground { get { return Bg; } }
        public override Drawing.Color ImageMarginGradientBegin { get { return Bg; } }
        public override Drawing.Color ImageMarginGradientMiddle { get { return Bg; } }
        public override Drawing.Color ImageMarginGradientEnd { get { return Bg; } }
        public override Drawing.Color MenuBorder { get { return Line; } }
        public override Drawing.Color MenuItemBorder { get { return Line; } }
        public override Drawing.Color MenuItemSelected { get { return Hover; } }
        public override Drawing.Color MenuItemSelectedGradientBegin { get { return Hover; } }
        public override Drawing.Color MenuItemSelectedGradientEnd { get { return Hover; } }
        public override Drawing.Color SeparatorDark { get { return Line; } }
        public override Drawing.Color SeparatorLight { get { return Line; } }
        public override Drawing.Color CheckBackground { get { return Hover; } }
        public override Drawing.Color CheckSelectedBackground { get { return Hover; } }
    }

    sealed class HudWindow : Window
    {
        const string PipeName = "QoderHud";
        const int WM_MOUSEACTIVATE = 0x0021;
        const int WM_MOUSEWHEEL = 0x020A;
        const int MA_NOACTIVATE = 3;
        const int WS_EX_TOOLWINDOW = 0x00000080;
        const int WS_EX_NOACTIVATE = 0x08000000;
        const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        const int DWMWA_BORDER_COLOR = 34;
        const int DWMWCP_DONOTROUND = 1;

        // 布局按 960x492 的小屏工作区设计
        const int DesignW = 960;
        const int DesignH = 492;
        const int HeaderTop = 14;
        const int ContentTop = 72;
        const int CardX = 20;
        const int CardW = 584;
        const int CardH = 127;
        const int CardGap = 8;
        const int SideX = 618;
        const int SideW = 322;
        const int SidePanelTop = 168;
        const int SidePanelH = 302;
        const int ListH = 398;

        static readonly Color BgTop = Color.FromArgb(255, 10, 14, 21);
        static readonly Color BgBottom = Color.FromArgb(255, 13, 19, 31);
        static readonly Color PanelTop = Color.FromArgb(255, 21, 28, 41);
        static readonly Color PanelBottom = Color.FromArgb(255, 15, 21, 34);
        static readonly Color PanelHoverTop = Color.FromArgb(255, 26, 35, 51);
        static readonly Color BorderNormal = Color.FromArgb(255, 34, 45, 64);
        static readonly Color BorderHover = Color.FromArgb(255, 56, 72, 100);
        static readonly Color TextPrimary = Color.FromArgb(255, 242, 247, 252);
        static readonly Color TextSecondary = Color.FromArgb(255, 154, 168, 188);
        static readonly Color TextTertiary = Color.FromArgb(255, 104, 118, 140);
        static readonly Color RunningColor = Color.FromArgb(255, 77, 157, 255);
        static readonly Color WaitingColor = Color.FromArgb(255, 255, 180, 67);
        static readonly Color DoneColor = Color.FromArgb(255, 62, 213, 152);
        static readonly Color StoppedColor = Color.FromArgb(255, 148, 163, 184);
        static readonly Color BadgeText = Color.FromArgb(255, 8, 13, 21);
        static readonly CultureInfo Zh = CultureInfo.GetCultureInfo("zh-CN");

        static readonly FontFamily UiFamily = new FontFamily("Microsoft YaHei UI, Microsoft YaHei, Segoe UI");
        static readonly FontFamily NumFamily = new FontFamily("Bahnschrift, Segoe UI");

        readonly List<HudTask> tasks = new List<HudTask>();
        readonly List<FeedItem> feed = new List<FeedItem>();
        readonly Dictionary<string, CardView> cards = new Dictionary<string, CardView>();
        readonly bool snapshotMode;
        readonly DispatcherTimer clockTimer;
        readonly DispatcherTimer pruneTimer;
        readonly DispatcherTimer reconcileTimer;
        readonly DispatcherTimer clickTimer;
        bool reconcileBusy;
        Forms.NotifyIcon tray;
        Forms.ContextMenuStrip mainMenu;
        Forms.ContextMenuStrip cardMenu;
        Drawing.Font menuFont;
        HudTask menuTask;
        HudTask detailTask;
        HudTask pendingClickTask;
        DateTime pendingClickAt = DateTime.MinValue;
        DateTime suppressClickUntil = DateTime.MinValue;
        int dblMs;
        bool topMost = true;
        bool pinHover;
        bool statusPulsing;
        bool amberOn;
        bool reallyExit;
        bool pipeStarted;
        bool closing;
        int wheelAccum;
        int scrollTarget;
        int scrollMax;
        DateTime lastEventAt = DateTime.MinValue;
        int totalEvents;
        IntPtr hwnd;

        Canvas root;
        Canvas mainView;
        Canvas headerCanvas;
        Canvas idleView;
        Canvas detailView;
        Canvas listViewport;
        Canvas cardsCanvas;
        Canvas sideCanvas;
        TranslateTransform cardsShift;
        TranslateTransform thumbShift;
        Border pinBorder;
        TextBlock pinText;
        Ellipse statusDot;
        TextBlock statusText;
        TextBlock clockText;
        TextBlock dateText;
        TextBlock idleClockText;
        TextBlock idleDateText;
        TextBlock idleHintText;
        TextBlock idleLastText;
        TextBlock hintText;
        Ellipse amberGlow;
        Border scrollTrack;
        Border scrollThumb;
        StatTile[] statTiles;
        FeedRow[] feedRows;
        TextBlock feedCountText;
        TextBlock feedFootLabel;
        TextBlock feedFootRight;
        PillButton backBtn;
        PillButton goBtn;
        PillButton closeBtn;
        TextBlock detailTitle;
        TextBlock detailMeta;
        Ellipse detailStateDot;
        TextBlock detailPrompt;
        TextBlock detailReply;
        TextBlock[] detailValueTexts;
        DetailLogRow[] detailLogRows;
        TextBlock detailLogEmpty;
        HudConfig config;
        Viewbox viewbox;
        bool displayWarned;

        public HudWindow() : this(false)
        {
        }

        public HudWindow(bool snapshotMode)
        {
            this.snapshotMode = snapshotMode;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            AllowsTransparency = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Width = DesignW;
            Height = DesignH;
            Background = Grad(BgTop, BgBottom);
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);
            Title = "Qoder HUD";

            if (snapshotMode)
            {
                config = new HudConfig();
                topMost = false;
            }
            else
            {
                HudConfigStore.EnsureFile();
                config = HudConfigStore.Load();
                TitleSource.SetOverrides(config.QoderDataDir, config.JetBrainsLogDir);
                topMost = LoadTopMostPref(config.TopMost);
                menuFont = MakeMenuFont();
            }
            dblMs = Math.Min(320, Forms.SystemInformation.DoubleClickTime);

            root = new Canvas();
            root.Width = DesignW;
            root.Height = DesignH;
            root.Background = Brushes.Transparent;
            root.ClipToBounds = true;
            viewbox = new Viewbox();
            viewbox.Child = root;
            viewbox.Stretch = Stretch.Uniform;
            viewbox.HorizontalAlignment = HorizontalAlignment.Center;
            viewbox.VerticalAlignment = VerticalAlignment.Center;
            Content = viewbox;

            BuildBackground();
            BuildMain();
            BuildDetail();
            if (!snapshotMode) BuildTray();

            root.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                if (detailTask == null)
                {
                    clickTimerStop();
                    pendingClickTask = null;
                }
            };
            root.MouseRightButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                if (detailTask != null) return;
                if (mainMenu != null)
                {
                    try { mainMenu.Show(Forms.Cursor.Position); }
                    catch { }
                }
                e.Handled = true;
            };

            clickTimer = new DispatcherTimer();
            clickTimer.Interval = TimeSpan.FromMilliseconds(dblMs);
            clickTimer.Tick += delegate
            {
                clickTimer.Stop();
                HudTask t = pendingClickTask;
                pendingClickTask = null;
                if (t != null) OpenDetail(t);
            };
            clockTimer = new DispatcherTimer();
            clockTimer.Interval = TimeSpan.FromSeconds(1);
            clockTimer.Tick += delegate
            {
                DateTime now = DateTime.Now;
                UpdateClock(now);
                foreach (CardView cv in cards.Values) cv.UpdateElapsed(now);
                if (detailTask != null) UpdateDetailElapsed(now);
                if (tasks.Count > 0) UpdateFeedFooter(now);
            };
            pruneTimer = new DispatcherTimer();
            pruneTimer.Interval = TimeSpan.FromMinutes(1);
            pruneTimer.Tick += delegate { Prune(); };
            reconcileTimer = new DispatcherTimer();
            reconcileTimer.Interval = TimeSpan.FromSeconds(3);
            reconcileTimer.Tick += delegate { ReconcileInterruptedTasks(); };

            if (!snapshotMode)
            {
                UpdateClock(DateTime.Now);
                Place();
                SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
                Loaded += OnLoaded;
            }
        }

        void OnLoaded(object sender, RoutedEventArgs e)
        {
            Place();
            ApplyTopMost();
            StartPipeServer();
            UpdateClock(DateTime.Now);
            RefreshAll();
            if (!clockTimer.IsEnabled) clockTimer.Start();
            if (!pruneTimer.IsEnabled) pruneTimer.Start();
            if (!reconcileTimer.IsEnabled) reconcileTimer.Start();
            double dpi = 1;
            try { dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip; }
            catch { }
            string tier = "?";
            try { tier = ((int)RenderCapability.Tier >> 16).ToString(); }
            catch { }
            Log("loaded hwnd=" + hwnd + " dpi=" + dpi + " renderTier=" + tier + " bounds=" + Left + "," + Top + " " + Width + "x" + Height
                + " display=" + config.Display + " position=" + config.Position + " scale=" + config.Scale.ToString(CultureInfo.InvariantCulture)
                + " topmost=" + topMost);
        }

        public void LoadDemoData(bool openDetail)
        {
            DateTime now = DateTime.Now;
            tasks.Clear();
            feed.Clear();
            foreach (CardView cv in cards.Values) cardsCanvas.Children.Remove(cv);
            cards.Clear();
            detailTask = null;
            totalEvents = 27;
            lastEventAt = now.AddSeconds(-18);

            HudTask waiting = new HudTask();
            waiting.SessionId = "7f31c92a";
            waiting.Title = "重构登录模块的会话刷新逻辑";
            waiting.Prompt = "登录过期后偶发 401，请检查令牌刷新与重试链路。";
            waiting.Cwd = "C:\\demo\\web-console";
            waiting.Model = "Qoder Pro";
            waiting.Origin = "JetBrains";
            waiting.OriginChain = "idea64.exe";
            waiting.Hint = "确认：编辑文件";
            waiting.PendingTool = "Edit";
            waiting.LastTool = "Edit";
            waiting.LastToolDetail = "src/auth/session.ts";
            waiting.State = TaskState.NeedsInput;
            waiting.StartedAt = now.AddMinutes(-14);
            waiting.StateSince = now.AddSeconds(-34);
            waiting.LastEvent = now.AddSeconds(-34);
            waiting.TitleReal = true;
            tasks.Add(waiting);

            HudTask running = new HudTask();
            running.SessionId = "b28e4d10";
            running.Title = "为订单服务补充批量导出接口";
            running.Prompt = "新增 CSV 导出，支持按日期筛选和分页。";
            running.Cwd = "C:\\demo\\order-api";
            running.Model = "Qwen3-Coder";
            running.Origin = "Desktop";
            running.LastTool = "Grep";
            running.LastToolDetail = "exportOrders";
            running.State = TaskState.Running;
            running.StartedAt = now.AddMinutes(-9);
            running.StateSince = now.AddSeconds(-252);
            running.LastEvent = now.AddMinutes(-3);
            running.TitleReal = true;
            tasks.Add(running);

            HudTask done = new HudTask();
            done.SessionId = "c91a6b3e";
            done.Title = "更新部署文档与回滚步骤";
            done.Prompt = "把蓝绿发布的回滚流程写进 README，覆盖失败判定、流量切换顺序、回滚命令和常见故障处理，并补充验证清单、负责人确认和演练记录，最后附上灰度观察指标与回归检查项。";
            done.Cwd = "C:\\demo\\infra-docs";
            done.Model = "Qoder Pro";
            done.Origin = "CLI";
            done.LastMessage = "已补充回滚步骤与命令示例，并加入失败判定、流量切换顺序、常见故障处理和验证清单。文档位于 docs/deploy.md，可直接用于下一次发布演练；同时补充了负责人确认、演练记录模板和发布后 24 小时观察项。";
            done.State = TaskState.Done;
            done.StartedAt = now.AddMinutes(-22);
            done.DoneAt = now.AddMinutes(-6);
            done.StateSince = done.DoneAt;
            done.LastEvent = done.DoneAt;
            done.TitleReal = true;
            tasks.Add(done);

            feed.Add(new FeedItem(now.AddSeconds(-34), waiting.SessionId, "等待确认 · 编辑文件", WaitingColor));
            feed.Add(new FeedItem(now.AddMinutes(-3), running.SessionId, "工具 · Grep · exportOrders", RunningColor));
            feed.Add(new FeedItem(now.AddMinutes(-6), done.SessionId, "完成 · 更新部署文档", DoneColor));
            feed.Add(new FeedItem(now.AddMinutes(-9), running.SessionId, "开始 · 订单导出接口", RunningColor));
            feed.Add(new FeedItem(now.AddMinutes(-14), waiting.SessionId, "开始 · 会话刷新逻辑", RunningColor));
            feed.Add(new FeedItem(now.AddMinutes(-15), done.SessionId, "工具 · Edit · docs/deploy.md", RunningColor));
            feed.Add(new FeedItem(now.AddMinutes(-18), done.SessionId, "工具 · Read · docs/deploy.md", RunningColor));
            feed.Add(new FeedItem(now.AddMinutes(-22), done.SessionId, "开始 · 部署文档与回滚", RunningColor));

            if (openDetail) detailTask = done;
            RefreshAll();
            UpdateClock(now);
        }

        public void SaveScreenshot(string path, double scale)
        {
            if (string.IsNullOrWhiteSpace(path)) path = "hud-preview.png";
            if (scale < 1.0) scale = 1.0;
            string fullPath;
            try { fullPath = Path.GetFullPath(path); }
            catch { fullPath = path; }
            string dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            Brush oldBackground = root.Background;
            root.Background = Background;
            try
            {
                TextOptions.SetTextFormattingMode(root, TextFormattingMode.Ideal);
                TextOptions.SetTextRenderingMode(root, TextRenderingMode.Grayscale);
                root.Measure(new Size(DesignW, DesignH));
                root.Arrange(new Rect(0, 0, DesignW, DesignH));
                root.UpdateLayout();

                int pixelW = (int)Math.Round(DesignW * scale);
                int pixelH = (int)Math.Round(DesignH * scale);
                RenderTargetBitmap bitmap = new RenderTargetBitmap(
                    pixelW, pixelH, 96.0 * scale, 96.0 * scale, PixelFormats.Pbgra32);
                bitmap.Render(root);

                PngBitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (FileStream stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    encoder.Save(stream);
                }
            }
            finally
            {
                root.Background = oldBackground;
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            hwnd = new WindowInteropHelper(this).Handle;
            int ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
            Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
            HwndSource src = HwndSource.FromHwnd(hwnd);
            if (src != null) src.AddHook(WndProc);
            ApplyDwm();
            ApplyTopMost();
            Place();
        }

        void ApplyDwm()
        {
            if (hwnd == IntPtr.Zero) return;
            int corner = DWMWCP_DONOTROUND;
            int border = unchecked((int)0xFFFFFFFE);
            int r1 = 0, r2 = 0;
            try { r1 = Native.DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, 4); }
            catch { }
            try { r2 = Native.DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, 4); }
            catch { }
            if (r1 != 0 || r2 != 0) Log("dwm attr corner=" + r1 + " border=" + r2);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!reallyExit)
            {
                e.Cancel = true;
                Hide();
                if (clockTimer != null) clockTimer.Stop();
                if (reconcileTimer != null) reconcileTimer.Stop();
                return;
            }
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            closing = true;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            if (clockTimer != null) clockTimer.Stop();
            if (pruneTimer != null) pruneTimer.Stop();
            if (reconcileTimer != null) reconcileTimer.Stop();
            if (clickTimer != null) clickTimer.Stop();
            if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
            if (mainMenu != null) { mainMenu.Dispose(); mainMenu = null; }
            if (cardMenu != null) { cardMenu.Dispose(); cardMenu = null; }
            if (menuFont != null) { menuFont.Dispose(); menuFont = null; }
            base.OnClosed(e);
        }

        // ---------------- UI construction ----------------

        void BuildBackground()
        {
            Canvas bg = new Canvas();
            bg.Width = DesignW;
            bg.Height = DesignH;
            bg.IsHitTestVisible = false;
            root.Children.Add(bg);
            bg.Children.Add(Glow(-240, -260, 820, 600, Color.FromArgb(22, 77, 157, 255)));
            bg.Children.Add(Glow(620, 300, 520, 420, Color.FromArgb(12, 62, 213, 152)));
            amberGlow = Glow(720, -200, 460, 400, Color.FromArgb(16, 255, 180, 67));
            amberGlow.Opacity = 0;
            bg.Children.Add(amberGlow);
        }

        void BuildMain()
        {
            mainView = new Canvas();
            mainView.Width = DesignW;
            mainView.Height = DesignH;
            mainView.Background = Brushes.Transparent;
            root.Children.Add(mainView);

            BuildHeader();
            BuildTasks();
            BuildSide();
            BuildIdle();
        }

        void BuildHeader()
        {
            headerCanvas = new Canvas();
            headerCanvas.Width = DesignW;
            headerCanvas.Height = ContentTop;
            mainView.Children.Add(headerCanvas);

            Border logo = new Border();
            logo.Width = 30;
            logo.Height = 30;
            logo.CornerRadius = new CornerRadius(9);
            logo.Background = Grad(Color.FromArgb(255, 96, 170, 255), Color.FromArgb(255, 46, 104, 255));
            Canvas.SetLeft(logo, 20);
            Canvas.SetTop(logo, 15);
            TextBlock logoText = Txt("Q", 18, Color.FromArgb(255, 255, 255, 255), true, true);
            logoText.HorizontalAlignment = HorizontalAlignment.Center;
            logoText.VerticalAlignment = VerticalAlignment.Center;
            logo.Child = logoText;
            headerCanvas.Children.Add(logo);

            StackPanel brand = new StackPanel();
            brand.Orientation = Orientation.Horizontal;
            brand.Height = 30;
            Canvas.SetLeft(brand, 60);
            Canvas.SetTop(brand, HeaderTop);
            TextBlock qoder = Txt("QODER", 22, TextPrimary, true, false);
            qoder.VerticalAlignment = VerticalAlignment.Center;
            TextBlock hud = Txt("HUD", 22, RunningColor, true, false);
            hud.VerticalAlignment = VerticalAlignment.Center;
            hud.Margin = new Thickness(6, 0, 0, 0);
            statusDot = new Ellipse();
            statusDot.Width = 10;
            statusDot.Height = 10;
            statusDot.Fill = Br(TextTertiary);
            statusDot.VerticalAlignment = VerticalAlignment.Center;
            statusDot.Margin = new Thickness(18, 0, 0, 0);
            statusText = Txt("", 17, TextSecondary, false, false);
            statusText.VerticalAlignment = VerticalAlignment.Center;
            statusText.Margin = new Thickness(8, 0, 0, 0);
            statusText.MaxWidth = 420;
            brand.Children.Add(qoder);
            brand.Children.Add(hud);
            brand.Children.Add(statusDot);
            brand.Children.Add(statusText);
            headerCanvas.Children.Add(brand);

            StackPanel clockPanel = new StackPanel();
            clockPanel.Orientation = Orientation.Horizontal;
            Canvas.SetRight(clockPanel, 90);
            Canvas.SetTop(clockPanel, 4);
            dateText = Txt("", 16, TextTertiary, false, false);
            dateText.Margin = new Thickness(0, 22, 0, 0);
            clockText = Txt("", 32, TextPrimary, true, true);
            clockText.Margin = new Thickness(18, 4, 0, 0);
            clockPanel.Children.Add(dateText);
            clockPanel.Children.Add(clockText);
            headerCanvas.Children.Add(clockPanel);

            pinBorder = new Border();
            pinBorder.Width = 56;
            pinBorder.Height = 26;
            pinBorder.CornerRadius = new CornerRadius(13);
            pinBorder.BorderThickness = new Thickness(2);
            pinBorder.Cursor = Cursors.Hand;
            Canvas.SetLeft(pinBorder, 884);
            Canvas.SetTop(pinBorder, 16);
            pinText = Txt("置顶", 14, TextPrimary, true, false);
            pinText.HorizontalAlignment = HorizontalAlignment.Center;
            pinText.VerticalAlignment = VerticalAlignment.Center;
            pinBorder.Child = pinText;
            pinBorder.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                ToggleTopMost();
            };
            pinBorder.MouseEnter += delegate
            {
                pinHover = true;
                UpdatePin();
            };
            pinBorder.MouseLeave += delegate
            {
                pinHover = false;
                UpdatePin();
            };
            headerCanvas.Children.Add(pinBorder);
            UpdatePin();
        }

        void BuildTasks()
        {
            listViewport = new Canvas();
            listViewport.Width = CardW;
            listViewport.Height = ListH;
            listViewport.ClipToBounds = true;
            Canvas.SetLeft(listViewport, CardX);
            Canvas.SetTop(listViewport, ContentTop);
            mainView.Children.Add(listViewport);

            cardsCanvas = new Canvas();
            cardsCanvas.Width = CardW;
            cardsCanvas.Height = 0;
            cardsShift = new TranslateTransform();
            cardsCanvas.RenderTransform = cardsShift;
            listViewport.Children.Add(cardsCanvas);

            hintText = Txt("单击任务查看详情 · Desktop 双击前往 Qoder", 14, TextTertiary, false, false);
            Canvas.SetLeft(hintText, CardX + 4);
            Canvas.SetTop(hintText, ContentTop);
            hintText.Visibility = Visibility.Collapsed;
            mainView.Children.Add(hintText);

            scrollTrack = new Border();
            scrollTrack.Width = 6;
            scrollTrack.Height = ListH;
            scrollTrack.CornerRadius = new CornerRadius(3);
            scrollTrack.Background = Br( Color.FromArgb(255, 28, 38, 56));
            scrollTrack.IsHitTestVisible = false;
            Canvas.SetLeft(scrollTrack, CardX + CardW + 3);
            Canvas.SetTop(scrollTrack, ContentTop);
            scrollTrack.Visibility = Visibility.Collapsed;
            mainView.Children.Add(scrollTrack);

            scrollThumb = new Border();
            scrollThumb.Width = 6;
            scrollThumb.Height = 30;
            scrollThumb.CornerRadius = new CornerRadius(3);
            scrollThumb.Background = Br(Color.FromArgb(255, 102, 122, 156));
            scrollThumb.IsHitTestVisible = false;
            thumbShift = new TranslateTransform();
            scrollThumb.RenderTransform = thumbShift;
            Canvas.SetLeft(scrollThumb, CardX + CardW + 3);
            Canvas.SetTop(scrollThumb, ContentTop);
            scrollThumb.Visibility = Visibility.Collapsed;
            mainView.Children.Add(scrollThumb);
        }

        void BuildSide()
        {
            sideCanvas = new Canvas();
            sideCanvas.Width = DesignW;
            sideCanvas.Height = DesignH;
            mainView.Children.Add(sideCanvas);

            statTiles = new StatTile[3];
            statTiles[0] = new StatTile(this, SideX, ContentTop, 102, 88, "运行中", RunningColor);
            statTiles[1] = new StatTile(this, SideX + 110, ContentTop, 102, 88, "待处理", WaitingColor);
            statTiles[2] = new StatTile(this, SideX + 220, ContentTop, 102, 88, "已完成", DoneColor);
            foreach (StatTile st in statTiles) sideCanvas.Children.Add(st.Root);

            Border panel = PanelBorder(SideX, SidePanelTop, SideW, SidePanelH, 14);
            sideCanvas.Children.Add(panel);
            Canvas pc = new Canvas();
            pc.Width = SideW;
            pc.Height = SidePanelH;
            panel.Child = pc;

            TextBlock title = Txt("最近活动", 20, TextPrimary, true, false);
            Canvas.SetLeft(title, 16);
            Canvas.SetTop(title, 13);
            pc.Children.Add(title);
            feedCountText = Txt("0 个事件", 14, TextTertiary, false, false);
            Canvas.SetRight(feedCountText, 16);
            Canvas.SetTop(feedCountText, 19);
            pc.Children.Add(feedCountText);

            Border line = new Border();
            line.Height = 2;
            line.Width = 290;
            line.Background = Br(Color.FromArgb(255, 28, 37, 54));
            Canvas.SetLeft(line, 16);
            Canvas.SetTop(line, 46);
            pc.Children.Add(line);

            feedRows = new FeedRow[5];
            for (int i = 0; i < feedRows.Length; i++)
            {
                feedRows[i] = new FeedRow(pc, 56 + i * 42);
            }

            Ellipse footDot = new Ellipse();
            footDot.Width = 8;
            footDot.Height = 8;
            footDot.Fill = Br(DoneColor);
            Canvas.SetLeft(footDot, 16);
            Canvas.SetTop(footDot, 278);
            pc.Children.Add(footDot);
            feedFootLabel = Txt(snapshotMode ? "演示数据" : "管道在线", 14, TextSecondary, false, false);
            Canvas.SetLeft(feedFootLabel, 30);
            Canvas.SetTop(feedFootLabel, 270);
            pc.Children.Add(feedFootLabel);
            feedFootRight = Txt("等待事件", 14, TextTertiary, false, false);
            Canvas.SetRight(feedFootRight, 16);
            Canvas.SetTop(feedFootRight, 270);
            pc.Children.Add(feedFootRight);
        }

        void BuildIdle()
        {
            idleView = new Canvas();
            idleView.Width = DesignW;
            idleView.Height = DesignH;
            idleView.IsHitTestVisible = false;
            mainView.Children.Add(idleView);
            idleView.Children.Add(Glow(DesignW / 2 - 260, 120, 520, 300, Color.FromArgb(18, 77, 157, 255)));

            idleClockText = Txt("", 92, TextPrimary, true, true);
            idleClockText.Width = DesignW;
            idleClockText.TextAlignment = TextAlignment.Center;
            Canvas.SetLeft(idleClockText, 0);
            Canvas.SetTop(idleClockText, 118);
            idleView.Children.Add(idleClockText);

            idleDateText = Txt("", 22, TextSecondary, false, false);
            idleDateText.Width = DesignW;
            idleDateText.TextAlignment = TextAlignment.Center;
            Canvas.SetLeft(idleDateText, 0);
            Canvas.SetTop(idleDateText, 266);
            idleView.Children.Add(idleDateText);

            Border pill = new Border();
            pill.Width = 132;
            pill.Height = 34;
            pill.CornerRadius = new CornerRadius(17);
            pill.Background = Br(Color.FromArgb(30, 77, 157, 255));
            pill.BorderBrush = Br(Color.FromArgb(150, 77, 157, 255));
            pill.BorderThickness = new Thickness(2);
            Canvas.SetLeft(pill, (DesignW - 132) / 2);
            Canvas.SetTop(pill, 318);
            TextBlock pillText = Txt("空闲 · 待命中", 16, Color.FromArgb(255, 140, 190, 255), true, false);
            pillText.HorizontalAlignment = HorizontalAlignment.Center;
            pillText.VerticalAlignment = VerticalAlignment.Center;
            pill.Child = pillText;
            idleView.Children.Add(pill);

            idleHintText = Txt("钩子已就绪 · 在 Qoder 里开始对话后，任务详情会显示在这里", 18, TextTertiary, false, false);
            idleHintText.Width = DesignW;
            idleHintText.TextAlignment = TextAlignment.Center;
            Canvas.SetLeft(idleHintText, 0);
            Canvas.SetTop(idleHintText, 378);
            idleView.Children.Add(idleHintText);

            idleLastText = Txt("", 18, TextSecondary, false, false);
            idleLastText.Width = DesignW;
            idleLastText.TextAlignment = TextAlignment.Center;
            Canvas.SetLeft(idleLastText, 0);
            Canvas.SetTop(idleLastText, 418);
            idleLastText.Visibility = Visibility.Collapsed;
            idleView.Children.Add(idleLastText);
        }

        void BuildDetail()
        {
            detailView = new Canvas();
            detailView.Width = DesignW;
            detailView.Height = DesignH;
            detailView.Background = Brushes.Transparent;
            detailView.Visibility = Visibility.Collapsed;
            root.Children.Add(detailView);
            detailView.MouseRightButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                CloseDetail();
            };

            backBtn = new PillButton(this, 20, 14, 104, 38, "返回", false);
            goBtn = new PillButton(this, 620, 14, 154, 38, "前往 Qoder", true);
            closeBtn = new PillButton(this, 786, 14, 154, 38, "关闭任务", false);
            backBtn.Click += delegate { CloseDetail(); };
            goBtn.Click += delegate { FocusQoder(detailTask); };
            closeBtn.Click += delegate { DismissDetailTask(); };
            detailView.Children.Add(backBtn);
            detailView.Children.Add(goBtn);
            detailView.Children.Add(closeBtn);

            detailTitle = Txt("", 26, TextPrimary, true, false);
            detailTitle.Width = 460;
            Canvas.SetLeft(detailTitle, 144);
            Canvas.SetTop(detailTitle, 10);
            detailView.Children.Add(detailTitle);

            detailStateDot = new Ellipse();
            detailStateDot.Width = 8;
            detailStateDot.Height = 8;
            Canvas.SetLeft(detailStateDot, 144);
            Canvas.SetTop(detailStateDot, 51);
            detailView.Children.Add(detailStateDot);

            detailMeta = Txt("", 14, TextSecondary, false, false);
            detailMeta.Width = 444;
            Canvas.SetLeft(detailMeta, 158);
            Canvas.SetTop(detailMeta, 45);
            detailView.Children.Add(detailMeta);

            Border left = PanelBorder(20, 68, 584, 402, 14);
            left.ClipToBounds = true;
            detailView.Children.Add(left);
            Canvas lc = new Canvas();
            lc.Width = 584;
            lc.Height = 402;
            left.Child = lc;

            TextBlock promptLabel = Txt("用户提问", 20, TextPrimary, true, false);
            Canvas.SetLeft(promptLabel, 16);
            Canvas.SetTop(promptLabel, 14);
            lc.Children.Add(promptLabel);

            detailPrompt = Txt("", 21, TextPrimary, false, false);
            detailPrompt.Width = 552;
            detailPrompt.Height = 140;
            detailPrompt.TextWrapping = TextWrapping.Wrap;
            detailPrompt.TextTrimming = TextTrimming.CharacterEllipsis;
            detailPrompt.LineHeight = 28;
            detailPrompt.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            detailPrompt.ClipToBounds = true;
            Canvas.SetLeft(detailPrompt, 16);
            Canvas.SetTop(detailPrompt, 42);
            lc.Children.Add(detailPrompt);

            TextBlock replyLabel = Txt("最近回复", 20, TextPrimary, true, false);
            Canvas.SetLeft(replyLabel, 16);
            Canvas.SetTop(replyLabel, 194);
            lc.Children.Add(replyLabel);

            detailReply = Txt("", 21, TextSecondary, false, false);
            detailReply.Width = 552;
            detailReply.Height = 168;
            detailReply.TextWrapping = TextWrapping.Wrap;
            detailReply.TextTrimming = TextTrimming.CharacterEllipsis;
            detailReply.LineHeight = 28;
            detailReply.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            detailReply.ClipToBounds = true;
            Canvas.SetLeft(detailReply, 16);
            Canvas.SetTop(detailReply, 222);
            lc.Children.Add(detailReply);

            Border info = PanelBorder(618, 68, 322, 232, 14);
            detailView.Children.Add(info);
            Canvas ic = new Canvas();
            ic.Width = 322;
            ic.Height = 232;
            info.Child = ic;
            TextBlock infoTitle = Txt("会话信息", 20, TextPrimary, true, false);
            Canvas.SetLeft(infoTitle, 16);
            Canvas.SetTop(infoTitle, 12);
            ic.Children.Add(infoTitle);
            string[] labels = { "项目", "来源", "会话", "模型", "开始", "用时", "状态" };
            detailValueTexts = new TextBlock[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                int y = 44 + i * 24;
                TextBlock lb = Txt(labels[i], 14, TextTertiary, false, false);
                Canvas.SetLeft(lb, 16);
                Canvas.SetTop(lb, y);
                ic.Children.Add(lb);
                TextBlock val = Txt("", 17, TextPrimary, false, false);
                val.Width = 236;
                Canvas.SetLeft(val, 70);
                Canvas.SetTop(val, y - 2);
                ic.Children.Add(val);
                detailValueTexts[i] = val;
            }

            Border log = PanelBorder(618, 312, 322, 158, 14);
            detailView.Children.Add(log);
            Canvas gc = new Canvas();
            gc.Width = 322;
            gc.Height = 158;
            log.Child = gc;
            TextBlock logTitle = Txt("动作记录", 20, TextPrimary, true, false);
            Canvas.SetLeft(logTitle, 16);
            Canvas.SetTop(logTitle, 12);
            gc.Children.Add(logTitle);
            detailLogRows = new DetailLogRow[4];
            for (int i = 0; i < detailLogRows.Length; i++)
            {
                detailLogRows[i] = new DetailLogRow(gc, 44 + i * 27);
            }
            detailLogEmpty = Txt("暂无动作记录", 17, TextTertiary, false, false);
            Canvas.SetLeft(detailLogEmpty, 16);
            Canvas.SetTop(detailLogEmpty, 46);
            gc.Children.Add(detailLogEmpty);
        }

        void BuildTray()
        {
            mainMenu = CreateMenu();
            mainMenu.Items.Add(MenuItem("显示 / 隐藏", delegate { ToggleVisible(); }));
            mainMenu.Items.Add(MenuItem("清空已完成", delegate { ClearDone(); }));
            mainMenu.Items.Add(new Forms.ToolStripSeparator());
            mainMenu.Items.Add(MenuItem("打开配置", delegate { OpenConfig(); }));
            mainMenu.Items.Add(MenuItem("重新加载配置", delegate { ReloadConfig(); }));
            mainMenu.Items.Add(MenuItem("打开日志", delegate { OpenLog(); }));
            mainMenu.Items.Add(new Forms.ToolStripSeparator());
            mainMenu.Items.Add(MenuItem("退出", delegate { Quit(); }));
            tray = new Forms.NotifyIcon();
            tray.Icon = Drawing.SystemIcons.Information;
            tray.Text = "Qoder HUD " + Program.Version;
            tray.Visible = true;
            tray.ContextMenuStrip = mainMenu;
            tray.DoubleClick += delegate { ToggleVisible(); };
        }

        Forms.ContextMenuStrip CreateMenu()
        {
            Forms.ContextMenuStrip m = new Forms.ContextMenuStrip();
            m.Renderer = new Forms.ToolStripProfessionalRenderer(new DarkMenuColorTable());
            m.BackColor = Drawing.Color.FromArgb(255, 21, 28, 41);
            m.ForeColor = Drawing.Color.FromArgb(255, 242, 247, 252);
            m.Font = menuFont;
            m.ShowImageMargin = false;
            m.DropShadowEnabled = false;
            m.Padding = new Forms.Padding(5);
            return m;
        }

        Forms.ToolStripMenuItem MenuItem(string text, EventHandler onClick)
        {
            Forms.ToolStripMenuItem item = new Forms.ToolStripMenuItem(text);
            item.ForeColor = Drawing.Color.FromArgb(255, 242, 247, 252);
            item.Padding = new Forms.Padding(10, 4, 10, 4);
            item.Click += onClick;
            return item;
        }

        static Drawing.Font MakeMenuFont()
        {
            try { return new Drawing.Font("Microsoft YaHei UI", 15f, Drawing.FontStyle.Regular, Drawing.GraphicsUnit.Pixel); }
            catch { return new Drawing.Font(Drawing.FontFamily.GenericSansSerif, 15f, Drawing.FontStyle.Regular, Drawing.GraphicsUnit.Pixel); }
        }

        // ---------------- placement / window state ----------------

        Forms.Screen PickScreen()
        {
            Forms.Screen primary = Forms.Screen.PrimaryScreen;
            string want = (config.Display ?? "auto").Trim();
            if (want.Equals("primary", StringComparison.OrdinalIgnoreCase)) return primary;
            if (!want.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                foreach (Forms.Screen s in Forms.Screen.AllScreens)
                {
                    if (s.DeviceName.Equals(want, StringComparison.OrdinalIgnoreCase)
                        || s.DeviceName.EndsWith(want, StringComparison.OrdinalIgnoreCase))
                        return s;
                }
                if (!displayWarned)
                {
                    Log("display not found: " + want + " fallback=auto");
                    displayWarned = true;
                }
            }
            Forms.Screen best = null;
            foreach (Forms.Screen s in Forms.Screen.AllScreens)
            {
                if (s.Primary) continue;
                if (best == null || s.Bounds.Width * s.Bounds.Height < best.Bounds.Width * best.Bounds.Height) best = s;
            }
            if (best != null) return best;
            if (primary != null) return primary;
            return Forms.Screen.AllScreens[0];
        }

        void Place()
        {
            Forms.Screen screen = PickScreen();
            Drawing.Rectangle wa = screen.WorkingArea;
            double dpi = 1.0;
            if (hwnd != IntPtr.Zero)
            {
                HwndSource src = HwndSource.FromHwnd(hwnd);
                if (src != null && src.CompositionTarget != null) dpi = src.CompositionTarget.TransformToDevice.M11;
            }
            if (dpi <= 0.1) dpi = 1.0;
            double availW = wa.Width / dpi;
            double availH = wa.Height / dpi;
            double want = config.Scale;
            double fit = Math.Min(want, Math.Min(availW / DesignW, availH / DesignH));
            if (fit < 0.1) fit = 0.1;
            double winW = Math.Round(DesignW * fit);
            double winH = Math.Round(DesignH * fit);

            string pos = (config.Position ?? "auto").ToLowerInvariant();
            if (pos == "auto") pos = screen.Primary ? "top-right" : "center";
            double margin = 20;
            double baseX = wa.X / dpi;
            double baseY = wa.Y / dpi;
            double left;
            double top;
            if (pos == "top-left")
            {
                left = baseX + margin;
                top = baseY + margin;
            }
            else if (pos == "top-right")
            {
                left = baseX + availW - winW - margin;
                top = baseY + margin;
            }
            else if (pos == "bottom-left")
            {
                left = baseX + margin;
                top = baseY + availH - winH - margin;
            }
            else if (pos == "bottom-right")
            {
                left = baseX + availW - winW - margin;
                top = baseY + availH - winH - margin;
            }
            else
            {
                left = baseX + (availW - winW) / 2;
                top = baseY + (availH - winH) / 2;
            }

            bool changed = double.IsNaN(Left) || double.IsNaN(Top)
                || Math.Abs(Left - left) > 0.1 || Math.Abs(Top - top) > 0.1
                || Math.Abs(Width - winW) > 0.1 || Math.Abs(Height - winH) > 0.1;
            Left = left;
            Top = top;
            Width = winW;
            Height = winH;
            if (changed)
                Log("place " + screen.DeviceName + " bounds=" + Math.Round(left) + "," + Math.Round(top)
                    + " " + winW + "x" + winH + " fit=" + fit.ToString("0.###", CultureInfo.InvariantCulture));
        }

        void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    displayWarned = false;
                    Place();
                    RefreshAll();
                }));
            }
            catch
            {
            }
        }

        void ApplyTopMost()
        {
            if (hwnd == IntPtr.Zero) return;
            Topmost = topMost;
            Native.SetWindowPos(hwnd, topMost ? Native.HWND_TOPMOST : Native.HWND_BOTTOM,
                0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            UpdatePin();
        }

        void ToggleTopMost()
        {
            topMost = !topMost;
            ApplyTopMost();
            SaveTopMostPref();
            Log("topmost " + (topMost ? "on" : "off"));
        }

        void UpdatePin()
        {
            if (pinBorder == null) return;
            if (topMost)
            {
                pinBorder.Background = Br(Color.FromArgb((byte)(pinHover ? 62 : 44), 77, 157, 255));
                pinBorder.BorderBrush = Br(Color.FromArgb(180, 77, 157, 255));
                if (pinText != null) pinText.Foreground = Br(TextPrimary);
            }
            else
            {
                pinBorder.Background = Br(Color.FromArgb((byte)(pinHover ? 28 : 14), 255, 255, 255));
                pinBorder.BorderBrush = Br(pinHover ? BorderHover : BorderNormal);
                if (pinText != null) pinText.Foreground = Br(TextTertiary);
            }
        }

        static bool LoadTopMostPref(bool fallback)
        {
            try
            {
                string p = AppPaths.TopMostFile;
                if (File.Exists(p)) return File.ReadAllText(p).Trim() != "0";
                string legacy = AppPaths.LegacyTopMostFile;
                if (File.Exists(legacy))
                {
                    bool value = File.ReadAllText(legacy).Trim() != "0";
                    try
                    {
                        Directory.CreateDirectory(AppPaths.ConfigDir);
                        File.WriteAllText(p, value ? "1" : "0");
                    }
                    catch
                    {
                    }
                    return value;
                }
            }
            catch
            {
            }
            return fallback;
        }

        void SaveTopMostPref()
        {
            if (snapshotMode) return;
            try
            {
                AppPaths.Ensure();
                File.WriteAllText(AppPaths.TopMostFile, topMost ? "1" : "0");
            }
            catch
            {
            }
        }

        void OpenConfig()
        {
            HudConfigStore.EnsureFile();
            OpenPath(AppPaths.ConfigFile);
        }

        void OpenLog()
        {
            Program.WriteLog("log opened from tray");
            OpenPath(AppPaths.LogFile);
        }

        static void OpenPath(string path)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = path;
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch
            {
                try { Process.Start("notepad.exe", path); }
                catch { }
            }
        }

        void ReloadConfig()
        {
            config = HudConfigStore.Load();
            TitleSource.SetOverrides(config.QoderDataDir, config.JetBrainsLogDir);
            displayWarned = false;
            Place();
            RefreshAll();
            Log("config reloaded display=" + config.Display + " position=" + config.Position
                + " scale=" + config.Scale.ToString(CultureInfo.InvariantCulture));
        }

        void ToggleVisible()
        {
            if (IsVisible)
            {
                Hide();
                clockTimer.Stop();
                reconcileTimer.Stop();
            }
            else ShowNoActivate();
        }

        void ShowNoActivate()
        {
            Place();
            if (!IsVisible) Show();
            Native.ShowWindow(hwnd, 8); // SW_SHOWNA
            ApplyTopMost();
            if (!clockTimer.IsEnabled) clockTimer.Start();
            if (!reconcileTimer.IsEnabled) reconcileTimer.Start();
            RefreshAll();
        }

        void Quit()
        {
            reallyExit = true;
            if (clockTimer != null) clockTimer.Stop();
            if (pruneTimer != null) pruneTimer.Stop();
            if (reconcileTimer != null) reconcileTimer.Stop();
            if (tray != null) tray.Visible = false;
            Application.Current.Shutdown();
        }

        void ClearDone()
        {
            for (int i = tasks.Count - 1; i >= 0; i--)
            {
                if (tasks[i].State == TaskState.Done) tasks.RemoveAt(i);
            }
            AfterTasksChanged();
        }

        void AfterTasksChanged()
        {
            if (detailTask != null && !tasks.Contains(detailTask))
            {
                detailTask = null;
                clickTimerStop();
            }
            if (tasks.Count == 0)
            {
                scrollTarget = 0;
                scrollMax = 0;
            }
            ClampScrollTarget();
            if (!IsVisible && tasks.Count > 0) ShowNoActivate();
            else RefreshAll();
        }

        // ---------------- data ----------------

        int RunningCount()
        {
            int n = 0;
            foreach (HudTask t in tasks) if (t.State == TaskState.Running) n++;
            return n;
        }

        int WaitingCount()
        {
            int n = 0;
            foreach (HudTask t in tasks) if (t.State == TaskState.NeedsInput) n++;
            return n;
        }

        int DoneCount()
        {
            int n = 0;
            foreach (HudTask t in tasks) if (t.State == TaskState.Done) n++;
            return n;
        }

        int StoppedCount()
        {
            int n = 0;
            foreach (HudTask t in tasks) if (t.State == TaskState.Done && t.Stopped) n++;
            return n;
        }

        static int ContentHeight(int count)
        {
            if (count <= 0) return 0;
            return count * CardH + (count - 1) * CardGap;
        }

        static int CardStep()
        {
            return CardH + CardGap;
        }

        void ClampScrollTarget()
        {
            scrollMax = Math.Max(0, ContentHeight(tasks.Count) - ListH);
            if (scrollTarget < 0) scrollTarget = 0;
            if (scrollTarget > scrollMax) scrollTarget = scrollMax;
        }

        static int Rank(HudTask t)
        {
            if (t.State == TaskState.NeedsInput) return 0;
            if (t.State == TaskState.Running) return 1;
            return 2;
        }

        List<HudTask> SortedTasks()
        {
            List<HudTask> list = new List<HudTask>(tasks);
            list.Sort(delegate (HudTask a, HudTask b)
            {
                int ra = Rank(a);
                int rb = Rank(b);
                if (ra != rb) return ra.CompareTo(rb);
                if (a.State == TaskState.NeedsInput) return a.StateSince.CompareTo(b.StateSince);
                return b.LastEvent.CompareTo(a.LastEvent);
            });
            return list;
        }

        // ---------------- pipe / events ----------------

        void StartPipeServer()
        {
            if (snapshotMode || pipeStarted) return;
            pipeStarted = true;
            Thread th = new Thread(delegate ()
            {
                while (true)
                {
                    NamedPipeServerStream server = null;
                    try
                    {
                        server = new NamedPipeServerStream(PipeName, PipeDirection.In, 16, PipeTransmissionMode.Byte, PipeOptions.None);
                        server.WaitForConnection();
                        ThreadPool.QueueUserWorkItem(delegate (object state)
                        {
                            NamedPipeServerStream s = (NamedPipeServerStream)state;
                            try
                            {
                                string text;
                                using (StreamReader sr = new StreamReader(s, new UTF8Encoding(false)))
                                {
                                    text = sr.ReadToEnd();
                                }
                                if (!string.IsNullOrEmpty(text) && !closing && !Dispatcher.HasShutdownStarted)
                                {
                                    try
                                    {
                                        Dispatcher.BeginInvoke(new Action<string>(HandleJson), new object[] { text });
                                    }
                                    catch
                                    {
                                    }
                                }
                            }
                            catch
                            {
                            }
                            finally
                            {
                                try { s.Dispose(); } catch { }
                            }
                        }, server);
                        server = null;
                    }
                    catch
                    {
                        Thread.Sleep(200);
                    }
                    finally
                    {
                        if (server != null)
                        {
                            try { server.Dispose(); } catch { }
                        }
                    }
                }
            });
            th.IsBackground = true;
            th.Name = "QoderHudPipe";
            th.Start();
        }

        void HandleJson(string json)
        {
            try
            {
                Dictionary<string, string> f = JsonProbe.ParseObject(json);
                if (f == null) return;
                string ev = Get(f, "hook_event_name");
                string sid = Get(f, "session_id");
                if (string.IsNullOrEmpty(sid)) sid = "unknown";
                if (ev.Length == 0) return;
                HudTask t = Find(sid);
                lastEventAt = DateTime.Now;
                totalEvents++;

                if (ev == "UserPromptSubmit")
                {
                    if (t == null) t = Ensure(sid, f);
                    string prompt = Squash(Get(f, "prompt"));
                    if (prompt.Length > 0)
                    {
                        t.Prompt = TrimTo(prompt, 400);
                        t.StartedAt = DateTime.Now;
                        t.LastMessage = "";
                        if (!t.TitleReal) t.Title = TrimTo(prompt, 90);
                    }
                    string sname = Squash(Get(f, "session_name"));
                    if (sname.Length > 0)
                    {
                        t.Title = TrimTo(sname, 90);
                        t.TitleReal = true;
                    }
                    string model = Get(f, "model");
                    if (model.Length > 0) t.Model = model;
                    SetState(t, TaskState.Running, "");
                    AddFeed(t.SessionId, "开始 · " + TrimTo(t.Title, 24), RunningColor);
                    RequestTitle(t);
                }
                else if (ev == "Stop")
                {
                    if (t == null) t = Ensure(sid, f);
                    t.LastMessage = TrimTo(Squash(Get(f, "last_assistant_message")), 400);
                    if (t.State != TaskState.Done)
                        AddFeed(t.SessionId, "完成 · " + TrimTo(t.Title, 24), DoneColor);
                    SetState(t, TaskState.Done, "");
                    t.LastTool = "";
                    t.LastToolDetail = "";
                    RequestTitle(t);
                }
                else if (ev == "SessionEnd")
                {
                    AddFeed(sid, "会话结束 · " + ShortId(sid), TextTertiary);
                    if (t != null)
                    {
                        tasks.Remove(t);
                        AfterTasksChanged();
                    }
                    return;
                }
                else if (ev == "PermissionRequest")
                {
                    if (t == null) t = Ensure(sid, f);
                    t.PendingTool = Get(f, "tool_name");
                    t.LastTool = t.PendingTool;
                    t.LastToolDetail = ToolDetail(t.PendingTool, Get(f, "tool_input"));
                    SetState(t, TaskState.NeedsInput, "确认：" + FriendlyTool(t.PendingTool));
                    ScrollToTop();
                    AddFeed(t.SessionId, "等待确认 · " + FriendlyTool(t.PendingTool), WaitingColor);
                }
                else if (ev == "Notification")
                {
                    string nt = Get(f, "notification_type");
                    if (nt == "permission_prompt" || nt == "elicitation_dialog")
                    {
                        if (t == null) t = Ensure(sid, f);
                        t.PendingTool = "";
                        string hint = nt == "permission_prompt" ? "等待你确认" : "等待你处理";
                        SetState(t, TaskState.NeedsInput, hint);
                        ScrollToTop();
                        AddFeed(t.SessionId, hint + " · " + TrimTo(t.Title, 22), WaitingColor);
                    }
                    else if (nt == "idle_prompt")
                    {
                        if (t != null && t.State == TaskState.Running)
                        {
                            SetState(t, TaskState.Done, "");
                            AddFeed(t.SessionId, "完成 · " + TrimTo(t.Title, 24), DoneColor);
                        }
                    }
                }
                else if (ev == "PermissionDenied")
                {
                    if (t != null && t.State == TaskState.NeedsInput)
                    {
                        SetState(t, TaskState.Running, "");
                        AddFeed(t.SessionId, "已拒绝 · 继续执行", RunningColor);
                    }
                }
                else if (ev == "PreToolUse")
                {
                    string tool = Get(f, "tool_name");
                    string detail = ToolDetail(tool, Get(f, "tool_input"));
                    if (tool == "AskUserQuestion" || tool == "ask_user_question")
                    {
                        if (t == null) t = Ensure(sid, f);
                        t.PendingTool = tool;
                        t.LastTool = tool;
                        t.LastToolDetail = detail;
                        SetState(t, TaskState.NeedsInput, "等待回答");
                        ScrollToTop();
                        AddFeed(t.SessionId, "等待回答 · " + TrimTo(t.Title, 22), WaitingColor);
                    }
                    else if (tool == "ExitPlanMode" || tool == "exit_plan_mode")
                    {
                        if (t == null) t = Ensure(sid, f);
                        t.PendingTool = tool;
                        t.LastTool = tool;
                        t.LastToolDetail = detail;
                        SetState(t, TaskState.NeedsInput, "等待确认计划");
                        ScrollToTop();
                        AddFeed(t.SessionId, "等待确认计划 · " + TrimTo(t.Title, 22), WaitingColor);
                    }
                    else
                    {
                        if (t == null) t = Ensure(sid, f);
                        t.LastTool = tool;
                        t.LastToolDetail = detail;
                        string text = "工具 · " + tool + (detail.Length > 0 ? " · " + detail : "");
                        AddFeed(t.SessionId, TrimTo(text, 60), RunningColor);
                    }
                }
                else if (ev == "PostToolUse" || ev == "PostToolUseFailure")
                {
                    string tool = Get(f, "tool_name");
                    string detail = ToolDetail(tool, Get(f, "tool_input"));
                    if (t == null) t = Ensure(sid, f);
                    t.LastTool = tool;
                    t.LastToolDetail = detail;
                    if (t.State == TaskState.NeedsInput &&
                        (string.IsNullOrEmpty(t.PendingTool) || t.PendingTool == tool))
                        SetState(t, TaskState.Running, "");
                    if (ev == "PostToolUseFailure")
                        AddFeed(t.SessionId, "工具失败 · " + tool, WaitingColor);
                }

                if (t != null)
                {
                    string o = Get(f, "_hud_origin");
                    if (o.Length > 0 && t.Origin != o)
                    {
                        t.Origin = o;
                        if (o == "JetBrains" && !t.TitleReal) RequestTitle(t);
                    }
                    string oc = Get(f, "_hud_chain");
                    if (oc.Length > 0 && t.OriginChain.Length == 0) t.OriginChain = oc;
                    t.LastEvent = DateTime.Now;
                }
                if (tasks.Count > 0 && !IsVisible) ShowNoActivate();
                else RefreshAll();
            }
            catch (Exception ex)
            {
                Log("handle error: " + ex.Message);
            }
        }

        static string Get(Dictionary<string, string> f, string key)
        {
            string v;
            return f.TryGetValue(key, out v) && v != null ? v : "";
        }

        HudTask Find(string sid)
        {
            for (int i = 0; i < tasks.Count; i++)
                if (tasks[i].SessionId == sid) return tasks[i];
            return null;
        }

        HudTask Ensure(string sid, Dictionary<string, string> f)
        {
            HudTask t = Find(sid);
            if (t != null) return t;
            t = new HudTask();
            t.SessionId = sid;
            t.Cwd = Get(f, "cwd");
            t.Origin = Get(f, "_hud_origin");
            if (t.Origin.Length == 0) t.Origin = "Unknown";
            t.OriginChain = Get(f, "_hud_chain");
            string prompt = Squash(Get(f, "prompt"));
            if (prompt.Length > 0) t.Prompt = TrimTo(prompt, 400);
            string sname = Squash(Get(f, "session_name"));
            string title = sname;
            if (title.Length > 0) t.TitleReal = true;
            if (title.Length == 0) title = prompt;
            if (title.Length == 0) title = Squash(Get(f, "last_assistant_message"));
            if (title.Length == 0) title = ShortDir(t.Cwd);
            t.Title = TrimTo(title, 90);
            string model = Get(f, "model");
            if (model.Length > 0) t.Model = model;
            tasks.Add(t);
            Program.WriteLog("new task " + ShortId(sid) + " [" + t.Origin + "] " + t.Title
                + " chain=" + t.OriginChain + " env=" + Get(f, "_hud_env") + " keys=" + KeyList(f));
            RequestTitle(t);
            return t;
        }

        static string KeyList(Dictionary<string, string> f)
        {
            StringBuilder sb = new StringBuilder();
            foreach (string k in f.Keys)
            {
                if (sb.Length > 0) sb.Append(',');
                sb.Append(k);
                if (sb.Length > 500) break;
            }
            return sb.ToString();
        }

        void SetState(HudTask t, TaskState state, string hint)
        {
            if (t.State == state && t.Hint == hint) return;
            if (state == TaskState.Done && t.State != TaskState.Done)
            {
                t.DonePulseStart = DateTime.Now;
                t.DoneAt = DateTime.Now;
            }
            Log("state " + ShortId(t.SessionId) + " " + state + (hint.Length > 0 ? " " + hint : ""));
            t.State = state;
            t.StateSince = DateTime.Now;
            t.Hint = hint;
            if (state != TaskState.NeedsInput) t.PendingTool = "";
            if (state != TaskState.Done) t.Stopped = false;
        }

        void ReconcileInterruptedTasks()
        {
            if (snapshotMode || closing || reconcileBusy) return;
            List<ReconcileProbe> probes = new List<ReconcileProbe>();
            foreach (HudTask t in tasks)
            {
                if (t.State != TaskState.Running && t.State != TaskState.NeedsInput) continue;
                if (string.IsNullOrEmpty(t.SessionId)) continue;
                ReconcileProbe probe = new ReconcileProbe();
                probe.SessionId = t.SessionId;
                probe.Origin = t.Origin;
                probe.StartedAt = t.StartedAt;
                probes.Add(probe);
            }
            if (probes.Count == 0) return;

            reconcileBusy = true;
            ThreadPool.QueueUserWorkItem(delegate (object state)
            {
                List<ReconcileHit> hits = new List<ReconcileHit>();
                foreach (ReconcileProbe probe in probes)
                {
                    DateTime interruptedAt;
                    if (!TitleSource.LastInterrupted(probe.SessionId, probe.Origin, probe.StartedAt, out interruptedAt)) continue;
                    if (interruptedAt <= probe.StartedAt) continue;
                    ReconcileHit hit = new ReconcileHit();
                    hit.SessionId = probe.SessionId;
                    hit.StartedAt = probe.StartedAt;
                    hit.InterruptedAt = interruptedAt;
                    hits.Add(hit);
                }
                try
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        reconcileBusy = false;
                        if (closing) return;
                        bool changed = false;
                        foreach (ReconcileHit hit in hits)
                        {
                            HudTask t = Find(hit.SessionId);
                            if (t == null) continue;
                            if (t.State != TaskState.Running && t.State != TaskState.NeedsInput) continue;
                            if (t.StartedAt != hit.StartedAt) continue;
                            MarkStopped(t, hit.InterruptedAt);
                            changed = true;
                        }
                        if (changed) RefreshAll();
                    }));
                }
                catch
                {
                    reconcileBusy = false;
                }
            });
        }

        void MarkStopped(HudTask t, DateTime when)
        {
            if (t == null || (t.State != TaskState.Running && t.State != TaskState.NeedsInput)) return;
            t.Stopped = true;
            SetState(t, TaskState.Done, "已停止");
            DateTime now = DateTime.Now;
            if (when > t.StartedAt && when <= now.AddMinutes(1))
            {
                t.StateSince = when;
                t.DoneAt = when;
                t.LastEvent = when;
            }
            t.LastTool = "";
            t.LastToolDetail = "";
            AddFeed(t.SessionId, "已停止 · " + TrimTo(t.Title, 24), StoppedColor);
            Log("stopped " + ShortId(t.SessionId) + " at " + when.ToString("HH:mm:ss"));
        }

        static string ShortId(string sid)
        {
            if (string.IsNullOrEmpty(sid)) return "?";
            return sid.Length <= 8 ? sid : sid.Substring(0, 8);
        }

        static string ShortDir(string cwd)
        {
            if (string.IsNullOrEmpty(cwd)) return "Qoder";
            try
            {
                string s = cwd.TrimEnd('\\', '/');
                int i = s.LastIndexOfAny(new char[] { '\\', '/' });
                return i >= 0 && i < s.Length - 1 ? s.Substring(i + 1) : s;
            }
            catch
            {
                return "Qoder";
            }
        }

        static string ShortPath(string p, int max)
        {
            if (string.IsNullOrEmpty(p)) return "";
            string s = p.Replace('\\', '/').TrimEnd('/');
            int i = s.LastIndexOf('/');
            if (i > 0)
            {
                int j = s.LastIndexOf('/', i - 1);
                if (j >= 0) s = s.Substring(j + 1);
            }
            return TrimTo(s, max);
        }

        static string FriendlyTool(string tool)
        {
            if (string.IsNullOrEmpty(tool)) return "工具调用";
            if (tool == "Bash") return "命令执行";
            if (tool == "AskUserQuestion" || tool == "ask_user_question") return "提问";
            if (tool == "ExitPlanMode" || tool == "exit_plan_mode") return "计划";
            if (tool == "Edit" || tool == "MultiEdit") return "编辑文件";
            if (tool == "Write") return "写入文件";
            if (tool == "Read") return "读取文件";
            if (tool == "Grep") return "搜索代码";
            return tool;
        }

        static string Squash(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder(s.Length);
            bool space = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c))
                {
                    if (!space) sb.Append(' ');
                    space = true;
                }
                else
                {
                    sb.Append(c);
                    space = false;
                }
            }
            return sb.ToString().Trim();
        }

        static string TrimTo(string s, int maxChars)
        {
            if (s == null) return "";
            if (maxChars < 1) maxChars = 1;
            if (s.Length <= maxChars) return s;
            return s.Substring(0, maxChars - 1) + "…";
        }

        static string Elapsed(TimeSpan ts)
        {
            if (ts < TimeSpan.Zero) ts = TimeSpan.Zero;
            if (ts.TotalHours >= 1)
                return ((int)ts.TotalHours) + ":" + ts.Minutes.ToString("00") + ":" + ts.Seconds.ToString("00");
            return ts.Minutes.ToString("00") + ":" + ts.Seconds.ToString("00");
        }

        static string Ago(DateTime t)
        {
            if (t == DateTime.MinValue) return "";
            double s = (DateTime.Now - t).TotalSeconds;
            if (s < 1) return "刚刚";
            if (s < 60) return (int)s + " 秒前";
            if (s < 3600) return (int)(s / 60) + " 分钟前";
            return (int)(s / 3600) + " 小时前";
        }

        static string NestedField(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || json[0] != '{') return "";
            Dictionary<string, string> d = JsonProbe.ParseObject(json);
            if (d == null) return "";
            string v;
            if (!d.TryGetValue(key, out v) || v == null) return "";
            if (v.Length == 0 || v == "null") return "";
            char c = v[0];
            if (c == '{' || c == '[') return "";
            return v;
        }

        static string ToolDetail(string tool, string inputJson)
        {
            if (string.IsNullOrEmpty(inputJson)) return "";
            string v = NestedField(inputJson, "command");
            if (v.Length == 0) v = NestedField(inputJson, "file_path");
            if (v.Length == 0) v = NestedField(inputJson, "path");
            if (v.Length == 0) v = NestedField(inputJson, "pattern");
            if (v.Length == 0) v = NestedField(inputJson, "description");
            if (v.Length == 0) v = NestedField(inputJson, "query");
            if (v.Length == 0) v = NestedField(inputJson, "url");
            if (v.Length == 0) v = NestedField(inputJson, "prompt");
            if (v.Length == 0) v = NestedField(inputJson, "skill");
            if (v.Length == 0) return "";
            v = Squash(v);
            if (v.IndexOf('\\') >= 0 || v.IndexOf('/') >= 0) v = ShortPath(v, 48);
            return TrimTo(v, 48);
        }

        void AddFeed(string sid, string text, Color color)
        {
            feed.Insert(0, new FeedItem(DateTime.Now, sid, text, color));
            if (feed.Count > 40) feed.RemoveRange(40, feed.Count - 40);
        }

        void RequestTitle(HudTask t)
        {
            if (snapshotMode || t == null) return;
            if (t.TitleBusy)
            {
                t.TitleAgain = true;
                return;
            }
            t.TitleBusy = true;
            string sid = t.SessionId;
            string origin = t.Origin;
            ThreadPool.QueueUserWorkItem(delegate (object state)
            {
                string title = TitleSource.Lookup(sid, origin);
                if (title.Length == 0)
                {
                    Thread.Sleep(5000);
                    title = TitleSource.Lookup(sid, origin);
                }
                try
                {
                    if (closing || Dispatcher.HasShutdownStarted) return;
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        HudTask cur = Find(sid);
                        if (cur == null) return;
                        cur.TitleBusy = false;
                        bool changed = false;
                        if (title.Length > 0 && cur.Title != title)
                        {
                            cur.Title = TrimTo(title, 90);
                            cur.TitleReal = true;
                            changed = true;
                            Log("title " + ShortId(sid) + " " + cur.Title);
                        }
                        if (changed) RefreshAll();
                        if (cur.TitleAgain && !cur.TitleReal)
                        {
                            cur.TitleAgain = false;
                            RequestTitle(cur);
                        }
                    }));
                }
                catch
                {
                }
            });
        }

        void Prune()
        {
            if (tasks.Count == 0) return;
            DateTime now = DateTime.Now;
            bool changed = false;
            for (int i = tasks.Count - 1; i >= 0; i--)
            {
                HudTask t = tasks[i];
                double hours = (now - t.LastEvent).TotalHours;
                bool stale = (t.State == TaskState.Done && (now - t.StateSince).TotalHours >= 1.0)
                    || (t.State == TaskState.NeedsInput && hours >= 6)
                    || (t.State == TaskState.Running && hours >= 4);
                if (stale)
                {
                    Log("prune " + ShortId(t.SessionId));
                    tasks.RemoveAt(i);
                    changed = true;
                }
            }
            if (changed) AfterTasksChanged();
        }

        // ---------------- interaction ----------------

        IntPtr WndProc(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == WM_MOUSEACTIVATE)
            {
                handled = true;
                return new IntPtr(MA_NOACTIVATE);
            }
            if (msg == WM_MOUSEWHEEL)
            {
                if (detailTask == null)
                {
                    long wp = w.ToInt64();
                    int wi = unchecked((int)wp);
                    int delta = (short)(wi >> 16);
                    wheelAccum += delta;
                    while (wheelAccum >= 120)
                    {
                        ScrollBy(-CardStep());
                        wheelAccum -= 120;
                    }
                    while (wheelAccum <= -120)
                    {
                        ScrollBy(CardStep());
                        wheelAccum += 120;
                    }
                }
                handled = true;
                return IntPtr.Zero;
            }
            return IntPtr.Zero;
        }

        void OnCardClick(HudTask t)
        {
            if (t == null || DateTime.Now < suppressClickUntil) return;
            bool dbl = pendingClickTask == t && pendingClickAt != DateTime.MinValue
                && (DateTime.Now - pendingClickAt).TotalMilliseconds <= dblMs;
            clickTimerStop();
            if (dbl)
            {
                pendingClickTask = null;
                suppressClickUntil = DateTime.Now.AddMilliseconds(250);
                FocusQoder(t);
                Log("focus " + ShortId(t.SessionId));
            }
            else
            {
                pendingClickTask = t;
                pendingClickAt = DateTime.Now;
                clickTimer.Interval = TimeSpan.FromMilliseconds(dblMs);
                clickTimer.Start();
            }
        }

        void clickTimerStop()
        {
            if (clickTimer != null) clickTimer.Stop();
        }

        void ShowCardMenu(HudTask t)
        {
            menuTask = t;
            if (cardMenu == null)
            {
                cardMenu = CreateMenu();
                cardMenu.Items.Add(MenuItem("前往 Qoder", delegate { FocusQoder(menuTask); }));
                cardMenu.Items.Add(MenuItem("关闭任务", delegate { CloseMenuTask(); }));
            }
            try { cardMenu.Show(Forms.Cursor.Position); }
            catch (Exception ex) { Log("menu error: " + ex.Message); }
        }

        void CloseMenuTask()
        {
            HudTask t = menuTask;
            if (t == null) return;
            menuTask = null;
            tasks.Remove(t);
            if (detailTask == t) detailTask = null;
            Log("dismiss " + ShortId(t.SessionId));
            AfterTasksChanged();
        }

        void OpenDetail(HudTask t)
        {
            if (t == null || !tasks.Contains(t)) return;
            detailTask = t;
            clickTimerStop();
            pendingClickTask = null;
            Log("detail " + ShortId(t.SessionId));
            RefreshAll();
        }

        void CloseDetail()
        {
            if (detailTask == null) return;
            detailTask = null;
            RefreshAll();
        }

        void DismissDetailTask()
        {
            HudTask t = detailTask;
            if (t == null) return;
            detailTask = null;
            tasks.Remove(t);
            Log("dismiss " + ShortId(t.SessionId));
            AfterTasksChanged();
        }

        void FocusQoder(HudTask task)
        {
            try
            {
                string origin = task != null ? task.Origin : "";
                IntPtr best = IntPtr.Zero;
                if (origin == "JetBrains" || origin == "CLI")
                {
                    ShowFocusError(origin);
                    return;
                }
                if (origin == "Desktop")
                {
                    best = FindMainWindow("Qoder", "Qoder");
                }
                if (best == IntPtr.Zero)
                {
                    Log("focus target not found origin=" + origin);
                    ShowFocusError(origin);
                    return;
                }
                ActivateWindow(best);
            }
            catch
            {
                ShowFocusError(task != null ? task.Origin : "");
            }
        }

        void ShowFocusError(string origin)
        {
            string message;
            if (origin == "JetBrains") message = "JetBrains 插件暂不支持自动跳转。";
            else if (origin == "CLI") message = "CLI 暂不支持自动跳转。";
            else if (origin == "Desktop") message = "未找到 Qoder 窗口。";
            else message = "无法识别任务来源，未执行跳转。";
            if (tray == null) return;
            try { tray.ShowBalloonTip(3000, "Qoder HUD", message, Forms.ToolTipIcon.Info); }
            catch { Program.WriteLog("focus notice failed origin=" + origin); }
        }

        static IntPtr FindMainWindow(string processName, string preferredTitle)
        {
            if (string.IsNullOrEmpty(processName)) return IntPtr.Zero;
            Process[] processes = Process.GetProcessesByName(processName);
            try
            {
                IntPtr best = IntPtr.Zero;
                foreach (Process p in processes)
                {
                    IntPtr h = p.MainWindowHandle;
                    if (h == IntPtr.Zero) continue;
                    if (best == IntPtr.Zero) best = h;
                    if (preferredTitle.Length > 0 && p.MainWindowTitle == preferredTitle) best = h;
                }
                return best;
            }
            finally
            {
                foreach (Process p in processes) p.Dispose();
            }
        }

        static void ActivateWindow(IntPtr hwnd)
        {
            if (Native.IsIconic(hwnd)) Native.ShowWindow(hwnd, 9); // SW_RESTORE
            if (!Native.SetForegroundWindow(hwnd))
            {
                uint fg = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), IntPtr.Zero);
                uint target = Native.GetWindowThreadProcessId(hwnd, IntPtr.Zero);
                if (fg != 0 && target != 0 && fg != target)
                {
                    Native.AttachThreadInput(fg, target, true);
                    try
                    {
                        Native.ShowWindow(hwnd, 5); // SW_SHOW
                        Native.SetForegroundWindow(hwnd);
                    }
                    finally
                    {
                        Native.AttachThreadInput(fg, target, false);
                    }
                }
                Native.SetForegroundWindow(hwnd);
            }
            Native.BringWindowToTop(hwnd);
        }

        // ---------------- rendering ----------------

        void RefreshAll()
        {
            if (detailTask != null && !tasks.Contains(detailTask)) detailTask = null;
            bool idle = tasks.Count == 0;
            idleView.Visibility = idle ? Visibility.Visible : Visibility.Collapsed;
            sideCanvas.Visibility = idle ? Visibility.Collapsed : Visibility.Visible;
            listViewport.Visibility = idle ? Visibility.Collapsed : Visibility.Visible;
            if (detailTask != null)
            {
                mainView.Visibility = Visibility.Collapsed;
                detailView.Visibility = Visibility.Visible;
            }
            else
            {
                mainView.Visibility = Visibility.Visible;
                detailView.Visibility = Visibility.Collapsed;
            }
            UpdateCards();
            UpdateSide();
            UpdateHeader();
            UpdateIdle();
            UpdateHint();
            UpdateAmber();
            if (detailTask != null) UpdateDetail();
        }

        void UpdateCards()
        {
            if (tasks.Count == 0)
            {
                cardsCanvas.Children.Clear();
                cards.Clear();
                cardsCanvas.Height = 0;
                scrollTarget = 0;
                scrollMax = 0;
                cardsShift.BeginAnimation(TranslateTransform.YProperty, null);
                cardsShift.Y = 0;
                thumbShift.BeginAnimation(TranslateTransform.YProperty, null);
                thumbShift.Y = 0;
                scrollTrack.Visibility = Visibility.Collapsed;
                scrollThumb.Visibility = Visibility.Collapsed;
                return;
            }

            List<HudTask> view = SortedTasks();
            HashSet<string> alive = new HashSet<string>();
            DateTime now = DateTime.Now;
            for (int i = 0; i < view.Count; i++)
            {
                HudTask t = view[i];
                alive.Add(t.SessionId);
                CardView cv;
                if (!cards.TryGetValue(t.SessionId, out cv))
                {
                    cv = new CardView(this);
                    cards[t.SessionId] = cv;
                    cardsCanvas.Children.Add(cv);
                }
                cv.Update(t, now);
                Canvas.SetTop(cv, i * CardStep());
            }

            List<string> dead = new List<string>();
            foreach (string sid in cards.Keys)
            {
                if (!alive.Contains(sid)) dead.Add(sid);
            }
            foreach (string sid in dead)
            {
                CardView cv = cards[sid];
                cardsCanvas.Children.Remove(cv);
                cards.Remove(sid);
            }

            cardsCanvas.Height = ContentHeight(view.Count);
            ClampScrollTarget();
            ApplyScrollInstant();
            UpdateScrollBar(view.Count);
        }

        void ApplyScrollInstant()
        {
            cardsShift.BeginAnimation(TranslateTransform.YProperty, null);
            cardsShift.Y = -scrollTarget;
        }

        void UpdateScrollBar(int count)
        {
            int total = ContentHeight(count);
            if (total <= ListH)
            {
                scrollTrack.Visibility = Visibility.Collapsed;
                scrollThumb.Visibility = Visibility.Collapsed;
                return;
            }
            scrollTrack.Visibility = Visibility.Visible;
            scrollThumb.Visibility = Visibility.Visible;
            int thumbH = Math.Max(30, (int)((long)ListH * ListH / total));
            scrollThumb.Height = thumbH;
            double travel = ListH - thumbH;
            double y = scrollMax > 0 ? travel * scrollTarget / scrollMax : 0;
            thumbShift.BeginAnimation(TranslateTransform.YProperty, null);
            thumbShift.Y = y;
        }

        void UpdateHeader()
        {
            int waiting = WaitingCount();
            int running = RunningCount();
            int done = DoneCount();
            int stopped = StoppedCount();
            Color dot;
            string status;
            if (waiting > 0)
            {
                dot = WaitingColor;
                status = waiting + " 个任务等待处理";
            }
            else if (running > 0)
            {
                dot = RunningColor;
                status = running + " 个任务运行中";
            }
            else if (done > 0)
            {
                int completed = done - stopped;
                dot = completed > 0 ? DoneColor : StoppedColor;
                if (completed > 0 && stopped > 0)
                    status = completed + " 个已完成 · " + stopped + " 个已停止";
                else if (completed > 0)
                    status = completed + " 个已完成";
                else
                    status = stopped + " 个任务已停止";
            }
            else
            {
                dot = Color.FromArgb(255, 106, 120, 140);
                status = "空闲";
            }
            if (tasks.Count > 0 && ContentHeight(tasks.Count) > ListH)
            {
                int first = 0;
                int last = 0;
                int offset = scrollTarget;
                for (int i = 0; i < tasks.Count; i++)
                {
                    int top = i * CardStep() - offset;
                    if (top + CardH > 0 && top < ListH)
                    {
                        if (first == 0) first = i + 1;
                        last = i + 1;
                    }
                }
                if (first > 0) status += " · " + first + "-" + last + " / " + tasks.Count;
            }
            SetText(statusText, status);
            SetFill(statusDot, dot);
            bool pulse = waiting > 0 || running > 0;
            if (snapshotMode)
            {
                statusDot.BeginAnimation(UIElement.OpacityProperty, null);
                statusDot.Opacity = 1;
            }
            else if (pulse)
            {
                if (!statusPulsing)
                {
                    statusPulsing = true;
                    DoubleAnimation a = new DoubleAnimation(0.55, 1.0, new Duration(TimeSpan.FromMilliseconds(1000)));
                    a.AutoReverse = true;
                    a.RepeatBehavior = RepeatBehavior.Forever;
                    a.EasingFunction = new SineEase();
                    Timeline.SetDesiredFrameRate(a, 8);
                    statusDot.BeginAnimation(UIElement.OpacityProperty, a);
                }
            }
            else if (statusPulsing)
            {
                statusPulsing = false;
                statusDot.BeginAnimation(UIElement.OpacityProperty, null);
                statusDot.Opacity = 1;
            }
        }

        void UpdateSide()
        {
            statTiles[0].SetCount(RunningCount(), RunningColor);
            statTiles[1].SetCount(WaitingCount(), WaitingColor);
            statTiles[2].SetCount(DoneCount(), DoneColor);

            for (int i = 0; i < feedRows.Length; i++)
            {
                if (i < feed.Count)
                {
                    FeedItem it = feed[i];
                    feedRows[i].Show(it);
                }
                else feedRows[i].Hide();
            }
            SetText(feedCountText, totalEvents + " 个事件");
            UpdateFeedFooter(DateTime.Now);
        }

        void UpdateFeedFooter(DateTime now)
        {
            string right = totalEvents > 0 ? totalEvents + " 事件 · " + Ago(lastEventAt) : "等待事件";
            SetText(feedFootRight, right);
        }

        void UpdateIdle()
        {
            if (tasks.Count != 0) return;
            if (idleLastText == null) return;
            DateTime now = DateTime.Now;
            SetText(idleClockText, now.ToString("HH:mm"));
            SetText(idleDateText, now.ToString("M月d日 dddd", Zh));
            if (feed.Count > 0)
            {
                idleLastText.Visibility = Visibility.Visible;
                SetText(idleLastText, "最近 · " + feed[0].Text);
            }
            else idleLastText.Visibility = Visibility.Collapsed;
        }

        void UpdateHint()
        {
            if (tasks.Count > 0 && tasks.Count < 3 && detailTask == null)
            {
                hintText.Visibility = Visibility.Visible;
                Canvas.SetTop(hintText, ContentTop + tasks.Count * CardStep() + 4);
                SetText(hintText, "单击任务查看详情 · Desktop 双击前往 Qoder");
            }
            else hintText.Visibility = Visibility.Collapsed;
        }

        void UpdateAmber()
        {
            bool on = WaitingCount() > 0;
            if (snapshotMode)
            {
                amberOn = on;
                amberGlow.BeginAnimation(UIElement.OpacityProperty, null);
                amberGlow.Opacity = on ? 1.0 : 0.0;
                return;
            }
            if (on == amberOn) return;
            amberOn = on;
            DoubleAnimation a = new DoubleAnimation(on ? 1.0 : 0.0, new Duration(TimeSpan.FromMilliseconds(400)));
            amberGlow.BeginAnimation(UIElement.OpacityProperty, a);
        }

        void UpdateClock(DateTime now)
        {
            SetText(clockText, now.ToString("HH:mm"));
            SetText(dateText, now.ToString("M月d日 dddd", Zh));
            if (idleView != null && idleView.Visibility == Visibility.Visible)
            {
                SetText(idleClockText, now.ToString("HH:mm"));
                SetText(idleDateText, now.ToString("M月d日 dddd", Zh));
            }
        }

        void UpdateDetail()
        {
            HudTask t = detailTask;
            if (t == null) return;
            bool stopped = t.State == TaskState.Done && t.Stopped;
            Color state = stopped ? StoppedColor : StateColor(t.State);
            string stateLabel = t.State == TaskState.NeedsInput ? "需要处理"
                : (t.State == TaskState.Running ? "运行中" : (stopped ? "已停止" : "已完成"));

            SetText(detailTitle, t.Title.Length > 0 ? t.Title : "未命名任务");
            StringBuilder meta = new StringBuilder();
            meta.Append(stateLabel);
            meta.Append(" · ").Append(OriginLabel(t.Origin));
            meta.Append(" · ").Append(ShortDir(t.Cwd));
            meta.Append(" · sid ").Append(ShortId(t.SessionId));
            if (!string.IsNullOrEmpty(t.Model)) meta.Append(" · ").Append(TrimTo(t.Model, 22));
            SetText(detailMeta, meta.ToString());
            SetFill(detailStateDot, state);

            SetText(detailPrompt, t.Prompt.Length > 0 ? t.Prompt : "（未捕获到用户输入，任务从工具事件恢复）");
            bool waiting = t.State == TaskState.NeedsInput;
            string reply = t.LastMessage.Length > 0 ? t.LastMessage
                : (stopped ? "任务已手动停止。" : (waiting && t.Hint.Length > 0 ? t.Hint : "—"));
            SetText(detailReply, reply);
            SetFg(detailReply, t.LastMessage.Length > 0 ? TextSecondary
                : (waiting ? Color.FromArgb(255, 255, 214, 153) : TextTertiary));

            string total = Elapsed(((t.State == TaskState.Done && t.DoneAt != DateTime.MinValue) ? t.DoneAt : DateTime.Now) - t.StartedAt);
            string[] values = {
                ShortDir(t.Cwd),
                OriginLabel(t.Origin),
                ShortId(t.SessionId),
                string.IsNullOrEmpty(t.Model) ? "—" : TrimTo(t.Model, 22),
                t.StartedAt.ToString("HH:mm:ss"),
                total,
                stateLabel
            };
            for (int i = 0; i < detailValueTexts.Length; i++)
                SetText(detailValueTexts[i], values[i]);

            int shown = 0;
            for (int i = 0; i < feed.Count && shown < detailLogRows.Length; i++)
            {
                FeedItem it = feed[i];
                if (it.SessionId != t.SessionId) continue;
                detailLogRows[shown].Show(it);
                shown++;
            }
            for (int i = shown; i < detailLogRows.Length; i++) detailLogRows[i].Hide();
            detailLogEmpty.Visibility = shown == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void UpdateDetailElapsed(DateTime now)
        {
            HudTask t = detailTask;
            if (t == null) return;
            string total = Elapsed(((t.State == TaskState.Done && t.DoneAt != DateTime.MinValue) ? t.DoneAt : now) - t.StartedAt);
            if (detailValueTexts != null && detailValueTexts.Length > 5) SetText(detailValueTexts[5], total);
        }

        // ---------------- scrolling ----------------

        void ScrollBy(int dy)
        {
            if (detailTask != null || tasks.Count == 0) return;
            int max = Math.Max(0, ContentHeight(tasks.Count) - ListH);
            int target = scrollTarget + dy;
            if (target > max) target = max;
            if (target < 0) target = 0;
            if (target == scrollTarget) return;
            scrollTarget = target;
            scrollMax = max;
            DoubleAnimation move = new DoubleAnimation(cardsShift.Y, -target, new Duration(TimeSpan.FromMilliseconds(200)));
            move.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            cardsShift.BeginAnimation(TranslateTransform.YProperty, move);
            int total = ContentHeight(tasks.Count);
            if (total > ListH)
            {
                int thumbH = Math.Max(30, (int)((long)ListH * ListH / total));
                double travel = ListH - thumbH;
                double targetY = max > 0 ? travel * target / max : 0;
                DoubleAnimation thumbMove = new DoubleAnimation(thumbShift.Y, targetY, new Duration(TimeSpan.FromMilliseconds(200)));
                thumbMove.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                thumbShift.BeginAnimation(TranslateTransform.YProperty, thumbMove);
            }
            UpdateHeader();
            Log("scroll " + target + "/" + max);
        }

        void ScrollToTop()
        {
            scrollTarget = 0;
            cardsShift.BeginAnimation(TranslateTransform.YProperty, null);
            cardsShift.Y = 0;
            thumbShift.BeginAnimation(TranslateTransform.YProperty, null);
            thumbShift.Y = 0;
            UpdateHeader();
        }

        // ---------------- static drawing helpers ----------------

        static SolidColorBrush Br(Color c)
        {
            SolidColorBrush b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        static LinearGradientBrush Grad(Color a, Color b)
        {
            LinearGradientBrush g = new LinearGradientBrush();
            g.StartPoint = new Point(0.5, 0);
            g.EndPoint = new Point(0.5, 1);
            g.GradientStops.Add(new GradientStop(a, 0));
            g.GradientStops.Add(new GradientStop(b, 1));
            g.Freeze();
            return g;
        }

        static Ellipse Glow(double x, double y, double w, double h, Color c)
        {
            RadialGradientBrush b = new RadialGradientBrush();
            b.GradientStops.Add(new GradientStop(Color.FromArgb(c.A, c.R, c.G, c.B), 0));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
            b.Freeze();
            Ellipse e = new Ellipse();
            e.Width = w;
            e.Height = h;
            e.Fill = b;
            e.IsHitTestVisible = false;
            Canvas.SetLeft(e, x);
            Canvas.SetTop(e, y);
            return e;
        }

        static TextBlock Txt(string text, double size, Color color, bool bold, bool num)
        {
            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontFamily = num ? NumFamily : UiFamily;
            t.FontSize = size;
            t.FontWeight = bold ? FontWeights.Bold : FontWeights.Normal;
            t.Foreground = Br(color);
            t.TextWrapping = TextWrapping.NoWrap;
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            t.IsHitTestVisible = false;
            return t;
        }

        static void SetText(TextBlock t, string s)
        {
            if (t != null && t.Text != s) t.Text = s;
        }

        static void SetFg(TextBlock t, Color c)
        {
            if (t == null) return;
            SolidColorBrush b = t.Foreground as SolidColorBrush;
            if (b == null || b.Color != c) t.Foreground = Br(c);
        }

        static void SetFill(Shape s, Color c)
        {
            if (s == null) return;
            SolidColorBrush b = s.Fill as SolidColorBrush;
            if (b == null || b.Color != c) s.Fill = Br(c);
        }

        static void SetBackground(Border b, Color c)
        {
            if (b == null) return;
            SolidColorBrush br = b.Background as SolidColorBrush;
            if (br == null || br.Color != c) b.Background = Br(c);
        }

        static Border PanelBorder(double x, double y, double w, double h, double radius)
        {
            Border b = new Border();
            b.Width = w;
            b.Height = h;
            b.CornerRadius = new CornerRadius(radius);
            b.BorderThickness = new Thickness(2);
            b.BorderBrush = Br(BorderNormal);
            b.Background = Grad(PanelTop, PanelBottom);
            Canvas.SetLeft(b, x);
            Canvas.SetTop(b, y);
            return b;
        }

        static Color StateColor(TaskState state)
        {
            if (state == TaskState.NeedsInput) return WaitingColor;
            if (state == TaskState.Done) return DoneColor;
            return RunningColor;
        }

        static Color OriginColor(string origin)
        {
            if (origin == "Desktop") return RunningColor;
            if (origin == "JetBrains") return Color.FromArgb(255, 192, 132, 252);
            if (origin == "CLI") return Color.FromArgb(255, 45, 212, 191);
            return TextTertiary;
        }

        static string OriginLabel(string origin)
        {
            if (origin == "Desktop") return "Qoder";
            if (origin == "JetBrains" || origin == "CLI") return origin;
            return "未知";
        }

        static string BuildMeta(HudTask t)
        {
            string project = ShortDir(t.Cwd);
            if (t.State == TaskState.NeedsInput)
            {
                string hint = t.Hint.Length > 0 ? t.Hint : "等待你处理";
                return hint + " · " + project + " · 单击详情 / 双击前往";
            }
            if (t.State == TaskState.Done)
            {
                if (t.Stopped) return project + " · 已手动停止 · 单击查看详情";
                if (t.LastMessage.Length > 0) return project + " · 回复：" + t.LastMessage;
                return project + " · 已完成 · 单击查看详情";
            }
            string action;
            if (t.LastTool.Length > 0)
                action = t.LastTool + (t.LastToolDetail.Length > 0 ? " " + t.LastToolDetail : "");
            else action = "准备中";
            string model = string.IsNullOrEmpty(t.Model) ? "" : " · " + TrimTo(t.Model, 20);
            return project + " · " + action + model;
        }

        void Log(string msg)
        {
            Program.WriteLog(msg);
        }

        sealed class ReconcileProbe
        {
            public string SessionId;
            public string Origin;
            public DateTime StartedAt;
        }

        sealed class ReconcileHit
        {
            public string SessionId;
            public DateTime StartedAt;
            public DateTime InterruptedAt;
        }

        // ---------------- nested UI parts ----------------

        sealed class CardView : Grid
        {
            readonly HudWindow owner;
            readonly Border baseBorder;
            readonly Border hoverBorder;
            readonly Border accent;
            readonly Border badge;
            readonly TextBlock badgeText;
            readonly Ellipse originDot;
            readonly TextBlock originText;
            readonly TextBlock chip;
            readonly TextBlock elapsedLabel;
            readonly TextBlock elapsedValue;
            readonly TextBlock title;
            readonly TextBlock prompt;
            readonly TextBlock meta;
            TaskState lastPulseState = (TaskState)(-1);
            DateTime lastPulseStart = DateTime.MinValue;
            HudTask task;

            public CardView(HudWindow owner)
            {
                this.owner = owner;
                Width = CardW;
                Height = CardH;
                Background = Brushes.Transparent;
                Cursor = Cursors.Hand;

                baseBorder = new Border();
                baseBorder.Width = CardW;
                baseBorder.Height = CardH;
                baseBorder.CornerRadius = new CornerRadius(14);
                baseBorder.BorderThickness = new Thickness(2);
                baseBorder.BorderBrush = Br(BorderNormal);
                baseBorder.Background = Grad(PanelTop, PanelBottom);
                Children.Add(baseBorder);

                hoverBorder = new Border();
                hoverBorder.Width = CardW;
                hoverBorder.Height = CardH;
                hoverBorder.CornerRadius = new CornerRadius(14);
                hoverBorder.BorderThickness = new Thickness(2);
                hoverBorder.BorderBrush = Br(BorderHover);
                hoverBorder.Background = Br(Color.FromArgb(18, 255, 255, 255));
                hoverBorder.Opacity = 0;
                hoverBorder.IsHitTestVisible = false;
                Children.Add(hoverBorder);

                Canvas content = new Canvas();
                content.Width = CardW;
                content.Height = CardH;
                Children.Add(content);

                accent = new Border();
                accent.Width = 6;
                accent.Height = CardH - 24;
                accent.CornerRadius = new CornerRadius(3);
                accent.Background = Br(RunningColor);
                Canvas.SetLeft(accent, 2);
                Canvas.SetTop(accent, 12);
                content.Children.Add(accent);

                StackPanel row = new StackPanel();
                row.Orientation = Orientation.Horizontal;
                row.Height = 24;
                Canvas.SetLeft(row, 20);
                Canvas.SetTop(row, 8);
                content.Children.Add(row);

                badge = new Border();
                badge.Height = 24;
                badge.CornerRadius = new CornerRadius(12);
                badge.Padding = new Thickness(11, 0, 11, 0);
                badge.Background = Br(RunningColor);
                badgeText = Txt("运行中", 15, BadgeText, true, false);
                badgeText.VerticalAlignment = VerticalAlignment.Center;
                badge.Child = badgeText;
                row.Children.Add(badge);

                StackPanel origin = new StackPanel();
                origin.Orientation = Orientation.Horizontal;
                origin.VerticalAlignment = VerticalAlignment.Center;
                origin.Margin = new Thickness(10, 0, 0, 0);
                originDot = new Ellipse();
                originDot.Width = 7;
                originDot.Height = 7;
                originDot.Fill = Br(TextTertiary);
                originDot.VerticalAlignment = VerticalAlignment.Center;
                originText = Txt("", 14, TextSecondary, false, false);
                originText.VerticalAlignment = VerticalAlignment.Center;
                originText.Margin = new Thickness(5, 0, 0, 0);
                origin.Children.Add(originDot);
                origin.Children.Add(originText);
                row.Children.Add(origin);

                chip = Txt("", 14, TextTertiary, false, false);
                chip.MaxWidth = 280;
                chip.VerticalAlignment = VerticalAlignment.Center;
                chip.Margin = new Thickness(10, 0, 0, 0);
                row.Children.Add(chip);

                StackPanel elapsed = new StackPanel();
                elapsed.Orientation = Orientation.Horizontal;
                Canvas.SetRight(elapsed, 18);
                Canvas.SetTop(elapsed, 6);
                elapsedLabel = Txt("用时", 14, TextTertiary, false, false);
                elapsedLabel.VerticalAlignment = VerticalAlignment.Center;
                elapsedLabel.Margin = new Thickness(0, 0, 6, 0);
                elapsedValue = Txt("", 22, TextPrimary, true, true);
                elapsedValue.VerticalAlignment = VerticalAlignment.Center;
                elapsed.Children.Add(elapsedLabel);
                elapsed.Children.Add(elapsedValue);
                content.Children.Add(elapsed);

                title = Txt("", 26, TextPrimary, true, false);
                title.Width = CardW - 40;
                Canvas.SetLeft(title, 20);
                Canvas.SetTop(title, 36);
                content.Children.Add(title);

                prompt = Txt("", 18, Color.FromArgb(255, 122, 137, 158), false, false);
                prompt.Width = CardW - 40;
                Canvas.SetLeft(prompt, 20);
                Canvas.SetTop(prompt, 73);
                prompt.Visibility = Visibility.Collapsed;
                content.Children.Add(prompt);

                meta = Txt("", 18, TextSecondary, false, false);
                meta.Width = CardW - 40;
                Canvas.SetLeft(meta, 20);
                Canvas.SetTop(meta, 76);
                content.Children.Add(meta);

                MouseEnter += delegate (object s, MouseEventArgs e)
                {
                    hoverBorder.BeginAnimation(UIElement.OpacityProperty,
                        new DoubleAnimation(1, new Duration(TimeSpan.FromMilliseconds(100))));
                };
                MouseLeave += delegate (object s, MouseEventArgs e)
                {
                    hoverBorder.BeginAnimation(UIElement.OpacityProperty,
                        new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(120))));
                };
                MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    owner.OnCardClick(task);
                };
                MouseRightButtonUp += delegate (object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    owner.ShowCardMenu(task);
                };
            }

            public void Update(HudTask t, DateTime now)
            {
                task = t;
                bool stopped = t.State == TaskState.Done && t.Stopped;
                Color state = stopped ? StoppedColor : StateColor(t.State);
                bool needs = t.State == TaskState.NeedsInput;
                string label = needs ? "需要处理"
                    : (t.State == TaskState.Running ? "运行中" : (stopped ? "已停止" : "已完成"));
                if (badgeText.Text != label)
                {
                    badgeText.Text = label;
                    badge.Background = Br(state);
                }
                SetBorder(baseBorder, needs ? Color.FromArgb(165, state.R, state.G, state.B)
                    : (stopped ? Color.FromArgb(110, state.R, state.G, state.B) : BorderNormal));

                SetFill(originDot, OriginColor(t.Origin));
                SetText(originText, OriginLabel(t.Origin));
                string chipText = ShortId(t.SessionId);
                if (!string.IsNullOrEmpty(t.Model)) chipText += " · " + TrimTo(t.Model, 20);
                SetText(chip, chipText);

                SetText(title, t.Title.Length > 0 ? t.Title : "未命名任务");
                SetFg(title, t.State == TaskState.Done ? Color.FromArgb(255, 199, 208, 221) : TextPrimary);

                bool showPrompt = t.TitleReal && t.Prompt.Length > 0 && t.Prompt != t.Title;
                prompt.Visibility = showPrompt ? Visibility.Visible : Visibility.Collapsed;
                if (showPrompt)
                {
                    SetText(prompt, "提问：" + t.Prompt);
                    Canvas.SetTop(meta, 100);
                }
                else Canvas.SetTop(meta, 76);
                SetText(meta, BuildMeta(t));
                SetFg(meta, needs ? Color.FromArgb(255, 255, 214, 153) : TextSecondary);

                SetBackground(accent, state);
                UpdateElapsed(now);
                UpdatePulse(t, now);
            }

            public void UpdateElapsed(DateTime now)
            {
                HudTask t = task;
                if (t == null) return;
                if (t.State == TaskState.Done)
                {
                    if (elapsedLabel.Visibility != Visibility.Visible) elapsedLabel.Visibility = Visibility.Visible;
                    SetText(elapsedValue, Elapsed(t.DoneAt - t.StartedAt));
                    SetFg(elapsedValue, TextSecondary);
                }
                else
                {
                    if (elapsedLabel.Visibility != Visibility.Collapsed) elapsedLabel.Visibility = Visibility.Collapsed;
                    SetText(elapsedValue, Elapsed(now - t.StateSince));
                    SetFg(elapsedValue, t.State == TaskState.NeedsInput
                        ? Color.FromArgb(255, 255, 205, 130)
                        : Color.FromArgb(235, 235, 242, 250));
                }
            }

            void UpdatePulse(HudTask t, DateTime now)
            {
                if (lastPulseState == t.State && lastPulseStart == t.DonePulseStart) return;
                lastPulseState = t.State;
                lastPulseStart = t.DonePulseStart;
                accent.BeginAnimation(UIElement.OpacityProperty, null);
                if (owner.snapshotMode)
                {
                    accent.Opacity = t.State == TaskState.Done ? 0.84 : 1.0;
                    return;
                }
                if (t.State == TaskState.Running)
                {
                    DoubleAnimation a = new DoubleAnimation(0.55, 0.95, new Duration(TimeSpan.FromMilliseconds(1200)));
                    a.AutoReverse = true;
                    a.RepeatBehavior = RepeatBehavior.Forever;
                    a.EasingFunction = new SineEase();
                    Timeline.SetDesiredFrameRate(a, 10);
                    accent.BeginAnimation(UIElement.OpacityProperty, a);
                }
                else if (t.State == TaskState.NeedsInput)
                {
                    DoubleAnimation a = new DoubleAnimation(0.45, 1.0, new Duration(TimeSpan.FromMilliseconds(900)));
                    a.AutoReverse = true;
                    a.RepeatBehavior = RepeatBehavior.Forever;
                    a.EasingFunction = new SineEase();
                    Timeline.SetDesiredFrameRate(a, 10);
                    accent.BeginAnimation(UIElement.OpacityProperty, a);
                }
                else if (t.DonePulseStart != DateTime.MinValue && (now - t.DonePulseStart).TotalMilliseconds < 1300)
                {
                    DoubleAnimation a = new DoubleAnimation(0.45, 0.84, new Duration(TimeSpan.FromMilliseconds(1300)));
                    a.EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut };
                    Timeline.SetDesiredFrameRate(a, 10);
                    accent.BeginAnimation(UIElement.OpacityProperty, a);
                }
                else accent.Opacity = 0.84;
            }

            static void SetBorder(Border b, Color c)
            {
                SolidColorBrush br = b.BorderBrush as SolidColorBrush;
                if (br == null || br.Color != c) b.BorderBrush = Br(c);
            }
        }

        sealed class StatTile
        {
            public readonly Border Root;
            readonly TextBlock number;
            readonly Color color;

            public StatTile(HudWindow owner, double x, double y, double w, double h, string label, Color color)
            {
                this.color = color;
                Root = PanelBorder(x, y, w, h, 12);
                Canvas c = new Canvas();
                c.Width = w;
                c.Height = h;
                Root.Child = c;
                number = Txt("0", 30, TextTertiary, true, true);
                number.Width = w;
                number.TextAlignment = TextAlignment.Center;
                Canvas.SetLeft(number, 0);
                Canvas.SetTop(number, 4);
                c.Children.Add(number);
                TextBlock lb = Txt(label, 16, TextSecondary, false, false);
                lb.Width = w;
                lb.TextAlignment = TextAlignment.Center;
                Canvas.SetLeft(lb, 0);
                Canvas.SetTop(lb, 52);
                c.Children.Add(lb);
            }

            public void SetCount(int count, Color c)
            {
                SetText(number, count.ToString());
                SetFg(number, count > 0 ? c : TextTertiary);
            }
        }

        sealed class FeedRow
        {
            readonly TextBlock time;
            readonly Ellipse dot;
            readonly TextBlock text;

            public FeedRow(Canvas parent, double y)
            {
                time = Txt("", 14, TextTertiary, false, true);
                Canvas.SetLeft(time, 16);
                Canvas.SetTop(time, y + 10);
                parent.Children.Add(time);
                dot = new Ellipse();
                dot.Width = 10;
                dot.Height = 10;
                dot.Fill = Br(TextTertiary);
                Canvas.SetLeft(dot, 58);
                Canvas.SetTop(dot, y + 15);
                parent.Children.Add(dot);
                text = Txt("", 17, TextPrimary, false, false);
                text.Width = 228;
                Canvas.SetLeft(text, 78);
                Canvas.SetTop(text, y + 9);
                parent.Children.Add(text);
                Hide();
            }

            public void Show(FeedItem it)
            {
                time.Visibility = Visibility.Visible;
                dot.Visibility = Visibility.Visible;
                text.Visibility = Visibility.Visible;
                SetText(time, it.Time.ToString("HH:mm"));
                SetFill(dot, it.Color);
                SetText(text, it.Text);
            }

            public void Hide()
            {
                time.Visibility = Visibility.Collapsed;
                dot.Visibility = Visibility.Collapsed;
                text.Visibility = Visibility.Collapsed;
            }
        }

        sealed class DetailLogRow
        {
            readonly TextBlock time;
            readonly Ellipse dot;
            readonly TextBlock text;

            public DetailLogRow(Canvas parent, double y)
            {
                time = Txt("", 14, TextTertiary, false, false);
                Canvas.SetLeft(time, 16);
                Canvas.SetTop(time, y);
                parent.Children.Add(time);
                dot = new Ellipse();
                dot.Width = 8;
                dot.Height = 8;
                dot.Fill = Br(TextTertiary);
                Canvas.SetLeft(dot, 58);
                Canvas.SetTop(dot, y + 5);
                parent.Children.Add(dot);
                text = Txt("", 17, TextPrimary, false, false);
                text.Width = 228;
                Canvas.SetLeft(text, 74);
                Canvas.SetTop(text, y - 2);
                parent.Children.Add(text);
                Hide();
            }

            public void Show(FeedItem it)
            {
                time.Visibility = Visibility.Visible;
                dot.Visibility = Visibility.Visible;
                text.Visibility = Visibility.Visible;
                SetText(time, it.Time.ToString("HH:mm"));
                SetFill(dot, it.Color);
                SetText(text, it.Text);
            }

            public void Hide()
            {
                time.Visibility = Visibility.Collapsed;
                dot.Visibility = Visibility.Collapsed;
                text.Visibility = Visibility.Collapsed;
            }
        }

        sealed class PillButton : Border
        {
            readonly bool primary;
            readonly TextBlock label;
            bool hover;

            public event EventHandler Click;

            public PillButton(HudWindow owner, double x, double y, double w, double h, string text, bool primary)
            {
                this.primary = primary;
                Width = w;
                Height = h;
                CornerRadius = new CornerRadius(10);
                BorderThickness = new Thickness(2);
                Cursor = Cursors.Hand;
                Canvas.SetLeft(this, x);
                Canvas.SetTop(this, y);
                label = Txt(text, 16, primary ? BadgeText : TextPrimary, true, false);
                label.HorizontalAlignment = HorizontalAlignment.Center;
                label.VerticalAlignment = VerticalAlignment.Center;
                Child = label;
                UpdateVisual();
                MouseEnter += delegate
                {
                    hover = true;
                    UpdateVisual();
                };
                MouseLeave += delegate
                {
                    hover = false;
                    UpdateVisual();
                };
                MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    if (Click != null) Click(this, EventArgs.Empty);
                };
            }

            void UpdateVisual()
            {
                if (primary)
                {
                    Background = Br(hover ? Color.FromArgb(255, 112, 178, 255) : RunningColor);
                    BorderBrush = Br(Color.FromArgb(0, 0, 0, 0));
                    label.Foreground = Br(BadgeText);
                }
                else
                {
                    Background = Br(hover ? PanelHoverTop : PanelTop);
                    BorderBrush = Br(hover ? BorderHover : BorderNormal);
                    label.Foreground = Br(TextPrimary);
                }
            }
        }
    }
}
