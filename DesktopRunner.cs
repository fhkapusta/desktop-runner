using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace DesktopRunnerApp
{
    static class Program
    {
        private const string MutexName = "DesktopRunner_SingleInstance_Mutex_desktop2_local";
        private const string StopEventName = "DesktopRunner_StopEvent_desktop2_local";

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Handle /stop command
            if (args != null && args.Length > 0)
            {
                string cmd = args[0].Trim().ToLowerInvariant();
                if (cmd == "/stop" || cmd == "-stop" || cmd == "--stop" || cmd == "/close" || cmd == "-close" || cmd == "/exit" || cmd == "stop")
                {
                    StopRunningInstances();
                    return;
                }
            }

            try
            {
                bool isNewInstance;
                using (Mutex mutex = new Mutex(true, MutexName, out isNewInstance))
                {
                    if (!isNewInstance)
                    {
                        DialogResult action = ShowAlreadyRunningDialog();
                        if (action == DialogResult.Abort) // Stop
                        {
                            StopRunningInstances();
                        }
                        else if (action == DialogResult.Retry) // Restart
                        {
                            StopRunningInstances();
                            Thread.Sleep(500);
                            try
                            {
                                Process.Start(Application.ExecutablePath);
                            }
                            catch { }
                        }
                        return;
                    }

                    string appDir = AppDomain.CurrentDomain.BaseDirectory;
                    string iniPath = Path.Combine(appDir, "config.ini");
                    Config config = Config.Load(iniPath, args);

                    using (EventWaitHandle stopEvent = new EventWaitHandle(false, EventResetMode.AutoReset, StopEventName))
                    {
                        stopEvent.Reset();
                        DesktopForm form = new DesktopForm(config, iniPath);

                        ThreadPool.RegisterWaitForSingleObject(stopEvent, (state, timedOut) =>
                        {
                            try
                            {
                                if (form != null && !form.IsDisposed)
                                {
                                    form.BeginInvoke(new Action(() => form.CloseApp()));
                                }
                            }
                            catch { }
                        }, null, -1, false);

                        Application.Run(form);
                    }
                }
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), ex.ToString()); } catch { }
            }
        }

        private static DialogResult ShowAlreadyRunningDialog()
        {
            Form dlg = new Form
            {
                Text = "Desktop Runner",
                ClientSize = new Size(425, 145),
                StartPosition = FormStartPosition.CenterScreen,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = true,
                Font = new Font("Segoe UI", 9f)
            };

            try
            {
                Icon appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (appIcon != null) dlg.Icon = appIcon;
            }
            catch { }

            PictureBox iconBox = new PictureBox
            {
                Location = new Point(20, 22),
                Size = new Size(32, 32),
                SizeMode = PictureBoxSizeMode.CenterImage,
                Image = SystemIcons.Information.ToBitmap()
            };
            dlg.Controls.Add(iconBox);

            Label lbl = new Label
            {
                Location = new Point(65, 18),
                Size = new Size(345, 65),
                Text = "Desktop Runner is already running.\n\nThe application icon is in the system tray near the clock.\nWhat would you like to do?",
                AutoSize = false
            };
            dlg.Controls.Add(lbl);

            Button btnStop = new Button
            {
                Text = "Stop",
                Location = new Point(110, 95),
                Size = new Size(95, 30),
                DialogResult = DialogResult.Abort
            };
            dlg.Controls.Add(btnStop);

            Button btnRestart = new Button
            {
                Text = "Restart",
                Location = new Point(215, 95),
                Size = new Size(95, 30),
                DialogResult = DialogResult.Retry
            };
            dlg.Controls.Add(btnRestart);

            Button btnClose = new Button
            {
                Text = "Close",
                Location = new Point(320, 95),
                Size = new Size(95, 30),
                DialogResult = DialogResult.Cancel
            };
            dlg.Controls.Add(btnClose);

            dlg.CancelButton = btnClose;
            dlg.AcceptButton = btnRestart;

            return dlg.ShowDialog();
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool WriteConsole(
            IntPtr hConsoleOutput,
            string lpBuffer,
            int nNumberOfCharsToWrite,
            out int lpNumberOfCharsWritten,
            IntPtr lpReserved);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(
            IntPtr hFile,
            byte[] lpBuffer,
            uint nNumberOfBytesToWrite,
            out uint lpNumberOfBytesWritten,
            IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();

        private const int ATTACH_PARENT_PROCESS = -1;
        private const int STD_OUTPUT_HANDLE = -11;

        private static void PrintConsoleMessage(string message)
        {
            AttachConsole(ATTACH_PARENT_PROCESS);
            IntPtr hStdOut = GetStdHandle(STD_OUTPUT_HANDLE);
            if (hStdOut != IntPtr.Zero && hStdOut.ToInt64() != -1)
            {
                string fullMsg = "\r\n" + message + "\r\n";
                int writtenChars;
                if (!WriteConsole(hStdOut, fullMsg, fullMsg.Length, out writtenChars, IntPtr.Zero))
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(fullMsg);
                    uint writtenBytes;
                    WriteFile(hStdOut, bytes, (uint)bytes.Length, out writtenBytes, IntPtr.Zero);
                }
            }
        }

        private static void StopRunningInstances()
        {
            bool stopped = false;
            try
            {
                EventWaitHandle stopEvent;
                if (EventWaitHandle.TryOpenExisting(StopEventName, out stopEvent))
                {
                    stopEvent.Set();
                    stopEvent.Close();
                    stopped = true;
                }
            }
            catch { }

            int currentPid = Process.GetCurrentProcess().Id;
            Process[] procs = Process.GetProcessesByName("DesktopRunner");

            foreach (Process p in procs)
            {
                if (p.Id != currentPid)
                {
                    stopped = true;
                    try
                    {
                        if (!p.WaitForExit(1500))
                        {
                            p.Kill();
                        }
                    }
                    catch { }
                }
            }

            if (stopped)
            {
                PrintConsoleMessage("[OK] Desktop Runner successfully stopped.");
            }
            else
            {
                PrintConsoleMessage("[INFO] Desktop Runner process not found (not running).");
            }

            FreeConsole();
        }
    }

    public class Config
    {
        public string Url { get; set; }
        public int ScreenIndex { get; set; }
        public bool EnableDevTools { get; set; }
        public bool EnableContextMenu { get; set; }

        public Config()
        {
            Url = "http://desktop2.local";
            ScreenIndex = 0;
            EnableDevTools = true;
            EnableContextMenu = false;
        }

        public static Config Load(string path, string[] args)
        {
            Config cfg = new Config();

            if (File.Exists(path))
            {
                try
                {
                    string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                    foreach (string rawLine in lines)
                    {
                        string line = rawLine.Trim();
                        if (line.StartsWith(";") || line.StartsWith("#") || !line.Contains("="))
                            continue;

                        int eq = line.IndexOf('=');
                        string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                        string val = line.Substring(eq + 1).Trim();

                        if (key == "url" && !string.IsNullOrEmpty(val))
                            cfg.Url = val;
                        else if (key == "screen")
                        {
                            int s;
                            if (int.TryParse(val, out s)) cfg.ScreenIndex = s;
                        }
                        else if (key == "devtools")
                        {
                            bool b;
                            if (bool.TryParse(val, out b)) cfg.EnableDevTools = b;
                        }
                        else if (key == "contextmenu")
                        {
                            bool b;
                            if (bool.TryParse(val, out b)) cfg.EnableContextMenu = b;
                        }
                    }
                }
                catch { }
            }

            if (args != null && args.Length > 0 && !string.IsNullOrEmpty(args[0]) && !args[0].StartsWith("/"))
            {
                cfg.Url = args[0];
            }

            return cfg;
        }
    }

    public class DesktopForm : Form
    {
        [DllImport("user32.dll")] private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
        [DllImport("user32.dll")] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string windowTitle);
        [DllImport("user32.dll")] private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")] private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT Point);
        [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);
        [DllImport("kernel32.dll")] private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);
        [DllImport("kernel32.dll")] private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);
        [DllImport("kernel32.dll")] private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out IntPtr lpNumberOfBytesWritten);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr hObject);

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private readonly Config config;
        private readonly string iniFilePath;
        private WebView2 webView;
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;
        private System.Windows.Forms.Timer retryTimer;
        private System.Windows.Forms.Timer searchRenderTimer;
        private bool isExiting = false;

        private IntPtr workerWHwnd = IntPtr.Zero;
        private IntPtr renderHwnd = IntPtr.Zero;
        private IntPtr hListView = IntPtr.Zero;
        private IntPtr hProcExplorer = IntPtr.Zero;
        private IntPtr remoteMem = IntPtr.Zero;
        private IntPtr hookId = IntPtr.Zero;
        private HookProc hookProc;

        private byte[] htiBytes = new byte[16];
        private bool isMouseDownOnIcon = false;

        private CoreWebView2Environment currentEnv = null;

        public DesktopForm(Config cfg, string iniPath)
        {
            this.config = cfg;
            this.iniFilePath = iniPath;

            InitializeWindow();
            InitializeTray();
            InitializeWebView();
        }

        private void InitializeWindow()
        {
            this.Text = "DesktopRunner";
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.BackColor = Color.Black;

            Screen[] screens = Screen.AllScreens;
            Screen targetScreen = Screen.PrimaryScreen;
            if (config.ScreenIndex >= 0 && config.ScreenIndex < screens.Length)
            {
                targetScreen = screens[config.ScreenIndex];
            }

            Rectangle sb = targetScreen.Bounds;
            this.SetBounds(sb.X, sb.Y, sb.Width, sb.Height);

            this.KeyPreview = true;
            this.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.F5)
                {
                    ReloadPage();
                }
            };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW
                return cp;
            }
        }

        private void InitializeTray()
        {
            trayMenu = new ContextMenuStrip();

            var titleItem = new ToolStripMenuItem("Desktop Runner")
            {
                Enabled = false,
                Font = new Font(FontFamily.GenericSansSerif, 9f, FontStyle.Bold)
            };
            trayMenu.Items.Add(titleItem);

            var urlItem = new ToolStripMenuItem(config.Url)
            {
                Enabled = false,
                ForeColor = Color.Gray
            };
            trayMenu.Items.Add(urlItem);

            trayMenu.Items.Add(new ToolStripSeparator());

            var reloadItem = new ToolStripMenuItem("Reload Page (F5)", null, (s, e) => ReloadPage());
            trayMenu.Items.Add(reloadItem);

            var googleAuthItem = new ToolStripMenuItem("Sign in to Google Account (Calendar)...", null, (s, e) => OpenGoogleSignInWindow());
            trayMenu.Items.Add(googleAuthItem);

            var openBrowserItem = new ToolStripMenuItem("Open in Browser", null, (s, e) =>
            {
                try { Process.Start(config.Url); } catch { }
            });
            trayMenu.Items.Add(openBrowserItem);

            var devToolsItem = new ToolStripMenuItem("Developer Tools (F12)", null, (s, e) =>
            {
                if (webView != null && webView.CoreWebView2 != null)
                {
                    webView.CoreWebView2.OpenDevToolsWindow();
                }
            });
            trayMenu.Items.Add(devToolsItem);

            var openConfigItem = new ToolStripMenuItem("Settings (config.ini)", null, (s, e) =>
            {
                try { Process.Start("notepad.exe", iniFilePath); } catch { }
            });
            trayMenu.Items.Add(openConfigItem);

            var aboutItem = new ToolStripMenuItem("About Desktop Runner...", null, (s, e) => ShowAboutDialog());
            trayMenu.Items.Add(aboutItem);

            trayMenu.Items.Add(new ToolStripSeparator());

            var exitItem = new ToolStripMenuItem("Exit Desktop Runner", null, (s, e) =>
            {
                CloseApp();
            });
            trayMenu.Items.Add(exitItem);

            Icon appIcon = null;
            try
            {
                appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch { }

            if (appIcon != null)
            {
                this.Icon = appIcon;
            }

            trayIcon = new NotifyIcon
            {
                Text = "Desktop Runner (" + config.Url + ")",
                ContextMenuStrip = trayMenu,
                Icon = appIcon ?? CreateAppIcon(),
                Visible = true
            };

            trayIcon.DoubleClick += (s, e) => ReloadPage();
        }

        
        private void ShowAboutDialog()
        {
            try
            {
                using (Form dlg = new Form
                {
                    Text = "About Desktop Runner",
                    ClientSize = new Size(390, 170),
                    StartPosition = FormStartPosition.CenterScreen,
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    MaximizeBox = false,
                    MinimizeBox = false,
                    ShowInTaskbar = true,
                    Font = new Font("Segoe UI", 9f),
                    Icon = this.Icon
                })
                {
                    PictureBox iconBox = new PictureBox
                    {
                        Location = new Point(25, 22),
                        Size = new Size(48, 48),
                        SizeMode = PictureBoxSizeMode.CenterImage
                    };

                    try
                    {
                        if (this.Icon != null)
                        {
                            using (Icon ico48 = new Icon(this.Icon, new Size(48, 48)))
                            {
                                iconBox.Image = ico48.ToBitmap();
                            }
                        }
                    }
                    catch
                    {
                        iconBox.Image = this.Icon != null ? this.Icon.ToBitmap() : SystemIcons.Application.ToBitmap();
                    }
                    dlg.Controls.Add(iconBox);

                    Label lblTitle = new Label
                    {
                        Location = new Point(88, 20),
                        Size = new Size(275, 26),
                        Text = "Desktop Runner",
                        Font = new Font("Segoe UI", 12f, FontStyle.Bold)
                    };
                    dlg.Controls.Add(lblTitle);

                    Label lblDesc = new Label
                    {
                        Location = new Point(89, 47),
                        Size = new Size(275, 20),
                        Text = "WebView2 Windows Desktop Host",
                        ForeColor = Color.DimGray,
                        Font = new Font("Segoe UI", 8.5f)
                    };
                    dlg.Controls.Add(lblDesc);

                    LinkLabel linkGit = new LinkLabel
                    {
                        Location = new Point(89, 73),
                        Size = new Size(285, 22),
                        Text = "https://github.com/fhkapusta/desktop-runner",
                        Font = new Font("Segoe UI", 9f),
                        LinkColor = Color.FromArgb(0, 102, 204),
                        ActiveLinkColor = Color.FromArgb(0, 153, 255),
                        Cursor = Cursors.Hand
                    };
                    linkGit.LinkClicked += (s, e) =>
                    {
                        try { Process.Start("https://github.com/fhkapusta/desktop-runner"); } catch { }
                    };
                    dlg.Controls.Add(linkGit);

                    Button btnClose = new Button
                    {
                        Text = "Close",
                        Location = new Point(285, 120),
                        Size = new Size(80, 28),
                        DialogResult = DialogResult.OK
                    };
                    dlg.Controls.Add(btnClose);

                    dlg.AcceptButton = btnClose;
                    dlg.CancelButton = btnClose;

                    dlg.ShowDialog();
                }
            }
            catch { }
        }


        private void OpenGoogleSignInWindow()
        {
            try
            {
                if (currentEnv == null)
                {
                    MessageBox.Show(
                        "WebView2 environment is still loading. Please try again in a few seconds.",
                        "Please Wait",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                Form authForm = new Form
                {
                    Text = "Sign in to Google Account (Google Calendar)",
                    Width = 650,
                    Height = 720,
                    StartPosition = FormStartPosition.CenterScreen,
                    ShowInTaskbar = true,
                    FormBorderStyle = FormBorderStyle.Sizable,
                    Icon = this.Icon
                };

                WebView2 authWv = new WebView2 { Dock = DockStyle.Fill };
                authForm.Controls.Add(authWv);

                authForm.FormClosed += (s, e) =>
                {
                    try { authWv.Dispose(); } catch { }
                    // Reload page so calendar iframe immediately picks up authenticated cookies
                    ReloadPage();
                };

                authForm.Load += async (s, e) =>
                {
                    try
                    {
                        await authWv.EnsureCoreWebView2Async(currentEnv);
                        authWv.CoreWebView2.Settings.AreDevToolsEnabled = config.EnableDevTools;
                        authWv.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;

                        authWv.CoreWebView2.WindowCloseRequested += (cs, ce) =>
                        {
                            try { if (!authForm.IsDisposed) authForm.Close(); } catch { }
                        };

                        authWv.CoreWebView2.Navigate("https://accounts.google.com/ServiceLogin?service=cl&continue=https%3A%2F%2Fcalendar.google.com%2Fcalendar%2Fu%2F0%2Fr");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Failed to open Google sign-in window:\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                };

                authForm.Show();
                authForm.BringToFront();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private Icon CreateAppIcon()
        {
            try
            {
                using (Bitmap bmp = new Bitmap(32, 32))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;

                    using (Pen p = new Pen(Color.FromArgb(0, 122, 204), 2.5f))
                    {
                        g.DrawRectangle(p, 3, 4, 25, 17);
                    }
                    using (Brush b = new SolidBrush(Color.FromArgb(20, 30, 45)))
                    {
                        g.FillRectangle(b, 5, 6, 21, 13);
                    }
                    using (Brush b = new SolidBrush(Color.FromArgb(255, 215, 0)))
                    {
                        g.FillEllipse(b, 12, 9, 7, 7);
                    }
                    using (Brush b = new SolidBrush(Color.FromArgb(0, 122, 204)))
                    {
                        g.FillRectangle(b, 13, 21, 5, 4);
                        g.FillRectangle(b, 9, 25, 13, 3);
                    }

                    IntPtr hIcon = bmp.GetHicon();
                    return Icon.FromHandle(hIcon);
                }
            }
            catch
            {
                return SystemIcons.Application;
            }
        }

        private void InitializeWebView()
        {
            webView = new WebView2
            {
                Dock = DockStyle.Fill,
                DefaultBackgroundColor = Color.Transparent
            };
            this.Controls.Add(webView);

            retryTimer = new System.Windows.Forms.Timer { Interval = 5000 };
            retryTimer.Tick += (s, e) =>
            {
                retryTimer.Stop();
                ReloadPage();
            };

            searchRenderTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            searchRenderTimer.Tick += (s, e) =>
            {
                if (renderHwnd == IntPtr.Zero)
                {
                    FindRenderHwnd();
                }
                else
                {
                    searchRenderTimer.Stop();
                }
            };

            this.Load += async (s, e) =>
            {
                AttachToWorkerW();
                await InitCoreWebView2Async();
                InitExplorerHitTest();
                InstallHook();
                searchRenderTimer.Start();
            };
        }

        private void AttachToWorkerW()
        {
            try
            {
                IntPtr progman = FindWindow("Progman", null);
                if (progman == IntPtr.Zero) return;

                IntPtr res;
                SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out res);

                IntPtr targetWorkerW = IntPtr.Zero;
                EnumWindows((hWnd, lParam) =>
                {
                    IntPtr defView = FindWindowEx(hWnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (defView != IntPtr.Zero)
                    {
                        targetWorkerW = FindWindowEx(IntPtr.Zero, hWnd, "WorkerW", null);
                        return false;
                    }
                    return true;
                }, IntPtr.Zero);

                if (targetWorkerW != IntPtr.Zero)
                {
                    workerWHwnd = targetWorkerW;
                    SetParent(this.Handle, workerWHwnd);
                    SetWindowPos(this.Handle, IntPtr.Zero, 0, 0, this.Width, this.Height, 0x0004 /* SWP_NOZORDER */ | 0x0010 /* SWP_NOACTIVATE */);
                }
            }
            catch { }
        }

        private void FindRenderHwnd()
        {
            try
            {
                if (webView != null && webView.Handle != IntPtr.Zero)
                {
                    EnumChildWindows(webView.Handle, (h, l) =>
                    {
                        StringBuilder cls = new StringBuilder(256);
                        GetClassName(h, cls, 256);
                        if (cls.ToString() == "Chrome_RenderWidgetHostHWND")
                        {
                            renderHwnd = h;
                            return false;
                        }
                        return true;
                    }, IntPtr.Zero);
                }
            }
            catch { }
        }

        private void InitExplorerHitTest()
        {
            try
            {
                IntPtr progman = FindWindow("Progman", null);
                IntPtr defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (defView == IntPtr.Zero)
                {
                    IntPtr w = IntPtr.Zero;
                    while ((w = FindWindowEx(IntPtr.Zero, w, "WorkerW", null)) != IntPtr.Zero)
                    {
                        defView = FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null);
                        if (defView != IntPtr.Zero) break;
                    }
                }

                if (defView != IntPtr.Zero)
                {
                    hListView = FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
                    if (hListView != IntPtr.Zero)
                    {
                        uint pid;
                        GetWindowThreadProcessId(hListView, out pid);
                        hProcExplorer = OpenProcess(0x001F0FFF /* PROCESS_ALL_ACCESS */, false, pid);
                        if (hProcExplorer != IntPtr.Zero)
                        {
                            remoteMem = VirtualAllocEx(hProcExplorer, IntPtr.Zero, 16, 0x1000 /* MEM_COMMIT */, 0x04 /* PAGE_READWRITE */);
                        }
                    }
                }
            }
            catch { }
        }

        private void InstallHook()
        {
            if (hookId == IntPtr.Zero)
            {
                hookProc = HookCallback;
                hookId = SetWindowsHookEx(14 /* WH_MOUSE_LL */, hookProc, IntPtr.Zero, 0);
            }
        }

        private bool CheckHitIcon(POINT screenPt)
        {
            try
            {
                if (hListView == IntPtr.Zero || hProcExplorer == IntPtr.Zero || remoteMem == IntPtr.Zero)
                {
                    InitExplorerHitTest();
                    if (hListView == IntPtr.Zero || hProcExplorer == IntPtr.Zero || remoteMem == IntPtr.Zero)
                        return false;
                }

                POINT lvPt = screenPt;
                ScreenToClient(hListView, ref lvPt);

                htiBytes[0] = (byte)(lvPt.x & 0xFF);
                htiBytes[1] = (byte)((lvPt.x >> 8) & 0xFF);
                htiBytes[2] = (byte)((lvPt.x >> 16) & 0xFF);
                htiBytes[3] = (byte)((lvPt.x >> 24) & 0xFF);
                htiBytes[4] = (byte)(lvPt.y & 0xFF);
                htiBytes[5] = (byte)((lvPt.y >> 8) & 0xFF);
                htiBytes[6] = (byte)((lvPt.y >> 16) & 0xFF);
                htiBytes[7] = (byte)((lvPt.y >> 24) & 0xFF);

                IntPtr written;
                WriteProcessMemory(hProcExplorer, remoteMem, htiBytes, 16, out written);
                IntPtr res = SendMessage(hListView, 0x1012 /* LVM_HITTEST */, IntPtr.Zero, remoteMem);

                return (res.ToInt32() >= 0);
            }
            catch
            {
                return false;
            }
        }

        private void CloseActiveDropdownInWeb()
        {
            try
            {
                if (webView != null && !webView.IsDisposed && webView.CoreWebView2 != null)
                {
                    this.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            webView.CoreWebView2.ExecuteScriptAsync("if(window.closeCustomSelect)window.closeCustomSelect();");
                        }
                        catch { }
                    }));
                }
            }
            catch { }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && !isExiting)
            {
                int msg = (int)wParam;

                // 1. NEVER intercept or block WM_MOUSEMOVE!
                // Mouse moves 100% naturally at full hardware speed without cursor locking or element flickering!
                if (msg == 0x0200 /* WM_MOUSEMOVE */)
                {
                    return CallNextHookEx(hookId, nCode, wParam, lParam);
                }

                MSLLHOOKSTRUCT hookStruct = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));

                // 2. If left button was pressed on an icon and is now being released, pass UP to Explorer!
                if (msg == 0x0202 /* WM_LBUTTONUP */ && isMouseDownOnIcon)
                {
                    isMouseDownOnIcon = false;
                    return CallNextHookEx(hookId, nCode, wParam, lParam);
                }

                IntPtr wndUnderMouse = WindowFromPoint(hookStruct.pt);
                StringBuilder sb = new StringBuilder(256);
                GetClassName(wndUnderMouse, sb, 256);
                string cls = sb.ToString();

                // If cursor is over desktop shell window
                if (cls == "SysListView32" || cls == "SHELLDLL_DefView" || cls == "WorkerW" || cls == "Progman")
                {
                    bool hitIcon = CheckHitIcon(hookStruct.pt);

                    // If click is on a desktop shortcut/icon:
                    if (hitIcon)
                    {
                        if (msg == 0x0201 /* WM_LBUTTONDOWN */)
                        {
                            isMouseDownOnIcon = true;
                            CloseActiveDropdownInWeb();
                        }
                        return CallNextHookEx(hookId, nCode, wParam, lParam);
                    }

                    // If click is on empty desktop (our web dashboard):
                    if (renderHwnd == IntPtr.Zero)
                    {
                        FindRenderHwnd();
                    }

                    if (renderHwnd != IntPtr.Zero)
                    {
                        Point clientPt = this.PointToClient(new Point(hookStruct.pt.x, hookStruct.pt.y));
                        IntPtr lp = (IntPtr)((clientPt.Y << 16) | (clientPt.X & 0xFFFF));

                        if (msg == 0x0201 /* WM_LBUTTONDOWN */)
                        {
                            isMouseDownOnIcon = false;
                            // Synchronize Chromium cursor position right at the click moment
                            PostMessage(renderHwnd, 0x0200 /* WM_MOUSEMOVE */, IntPtr.Zero, lp);
                            PostMessage(renderHwnd, (uint)msg, (IntPtr)1, lp);
                            // Pass to Explorer so shortcut focus/selection clears and right-click menu dismisses!
                            return CallNextHookEx(hookId, nCode, wParam, lParam);
                        }
                        else if (msg == 0x0202 /* WM_LBUTTONUP */)
                        {
                            PostMessage(renderHwnd, (uint)msg, IntPtr.Zero, lp);
                            return CallNextHookEx(hookId, nCode, wParam, lParam);
                        }
                        else if (msg == 0x0203 /* WM_LBUTTONDBLCLK */)
                        {
                            PostMessage(renderHwnd, (uint)msg, (IntPtr)1, lp);
                            return CallNextHookEx(hookId, nCode, wParam, lParam);
                        }
                        else if (msg == 0x0204 /* WM_RBUTTONDOWN */ && config.EnableContextMenu)
                        {
                            PostMessage(renderHwnd, (uint)msg, (IntPtr)2, lp);
                            return (IntPtr)1;
                        }
                        else if (msg == 0x0205 /* WM_RBUTTONUP */ && config.EnableContextMenu)
                        {
                            PostMessage(renderHwnd, (uint)msg, IntPtr.Zero, lp);
                            return (IntPtr)1;
                        }
                        else if (msg == 0x020A /* WM_MOUSEWHEEL */)
                        {
                            PostMessage(renderHwnd, (uint)msg, (IntPtr)hookStruct.mouseData, lp);
                            return (IntPtr)1;
                        }
                    }
                }
                else
                {
                    // Click outside desktop (e.g. taskbar, another app)
                    if (msg == 0x0201 /* WM_LBUTTONDOWN */)
                    {
                        CloseActiveDropdownInWeb();
                    }
                }
            }

            return CallNextHookEx(hookId, nCode, wParam, lParam);
        }

        private bool IsGoogleAuthOrCalendarPopup(string uri, CoreWebView2FrameInfo frameInfo, CoreWebView2WindowFeatures features)
        {
            if (!string.IsNullOrEmpty(uri))
            {
                string u = uri.ToLowerInvariant();
                if (u.Contains("accounts.google.") ||
                    u.Contains("google.com/accounts") ||
                    u.Contains("google.com/signin") ||
                    u.Contains("storageaccess") ||
                    u.Contains("myaccount.google.") ||
                    u.Contains("accounts.youtube."))
                {
                    return true;
                }
            }

            if (frameInfo != null && !string.IsNullOrEmpty(frameInfo.Source))
            {
                string frameSrc = frameInfo.Source.ToLowerInvariant();
                if (frameSrc.Contains("calendar.google.") || frameSrc.Contains("accounts.google."))
                {
                    // If opened as a sized popup window (e.g. window.open dialog)
                    if (features != null && features.HasSize)
                    {
                        return true;
                    }
                    if (!string.IsNullOrEmpty(uri) && uri.ToLowerInvariant().Contains("storageaccess"))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private async Task InitCoreWebView2Async()
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string userDataFolder = Path.Combine(localAppData, "DesktopRunner", "Cache");
                if (!Directory.Exists(userDataFolder))
                {
                    Directory.CreateDirectory(userDataFolder);
                }

                CoreWebView2EnvironmentOptions options = new CoreWebView2EnvironmentOptions();
                options.EnableTrackingPrevention = false;
                options.AllowSingleSignOnUsingOSPrimaryAccount = true;
                options.AdditionalBrowserArguments = "--disable-features=TrackingProtection3pcd,ThirdPartyStoragePartitioning --test-third-party-cookie-phaseout=false --unsafely-treat-insecure-origin-as-secure=http://desktop2.local";

                currentEnv = await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
                await webView.EnsureCoreWebView2Async(currentEnv);

                try
                {
                    webView.CoreWebView2.Profile.PreferredTrackingPreventionLevel = CoreWebView2TrackingPreventionLevel.None;
                }
                catch { }

                webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                webView.CoreWebView2.Settings.AreDevToolsEnabled = config.EnableDevTools;
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = config.EnableContextMenu;
                webView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = true;
                webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;

                // Auto-allow permission requests (storage access, notifications, etc.)
                webView.CoreWebView2.PermissionRequested += (s, e) =>
                {
                    e.State = CoreWebView2PermissionState.Allow;
                };

                // Handle new windows / popups
                webView.CoreWebView2.NewWindowRequested += async (s, e) =>
                {
                    string targetUri = e.Uri ?? "";
                    if (IsGoogleAuthOrCalendarPopup(targetUri, e.OriginalSourceFrameInfo, e.WindowFeatures))
                    {
                        var deferral = e.GetDeferral();
                        Form popupForm = null;
                        try
                        {
                            popupForm = new Form
                            {
                                Text = "Google Calendar - Authentication",
                                ShowInTaskbar = true,
                                StartPosition = FormStartPosition.CenterScreen,
                                FormBorderStyle = FormBorderStyle.Sizable,
                                Icon = this.Icon
                            };

                            int w = 620;
                            int h = 720;
                            if (e.WindowFeatures != null && e.WindowFeatures.HasSize && e.WindowFeatures.Width > 200 && e.WindowFeatures.Height > 200)
                            {
                                w = (int)Math.Min(e.WindowFeatures.Width, 1200);
                                h = (int)Math.Min(e.WindowFeatures.Height, 1000);
                            }
                            popupForm.ClientSize = new Size(w, h);

                            WebView2 popupWv = new WebView2 { Dock = DockStyle.Fill };
                            popupForm.Controls.Add(popupWv);

                            popupForm.FormClosed += (s2, e2) =>
                            {
                                try { popupWv.Dispose(); } catch { }
                                try
                                {
                                    if (webView != null && webView.CoreWebView2 != null)
                                    {
                                        webView.CoreWebView2.ExecuteScriptAsync(
                                            "var ifr = document.querySelector('iframe[src*=\"calendar.google.com\"]'); if (ifr) { ifr.src = ifr.src; }"
                                        );
                                    }
                                }
                                catch { }
                            };

                            await popupWv.EnsureCoreWebView2Async(currentEnv);

                            if (popupForm.IsDisposed)
                            {
                                deferral.Complete();
                                return;
                            }

                            popupWv.CoreWebView2.Settings.AreDevToolsEnabled = config.EnableDevTools;
                            popupWv.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;

                            popupWv.CoreWebView2.WindowCloseRequested += (s2, e2) =>
                            {
                                try
                                {
                                    if (!popupForm.IsDisposed) popupForm.Close();
                                }
                                catch { }
                            };

                            popupWv.CoreWebView2.NewWindowRequested += (s2, e2) =>
                            {
                                string childUri = e2.Uri ?? "";
                                if (!IsGoogleAuthOrCalendarPopup(childUri, e2.OriginalSourceFrameInfo, e2.WindowFeatures))
                                {
                                    e2.Handled = true;
                                    try { Process.Start(childUri); } catch { }
                                }
                            };

                            e.NewWindow = popupWv.CoreWebView2;
                            deferral.Complete();
                            popupForm.Show();
                            popupForm.BringToFront();
                        }
                        catch
                        {
                            try { deferral.Complete(); } catch { }
                            if (popupForm != null && !popupForm.IsDisposed)
                            {
                                try { popupForm.Dispose(); } catch { }
                            }
                        }
                        return;
                    }

                    // Open other links with target="_blank" in user's default browser
                    e.Handled = true;
                    try
                    {
                        if (!string.IsNullOrEmpty(e.Uri))
                        {
                            Process.Start(e.Uri);
                        }
                    }
                    catch { }
                };

                // Auto retry on connection failure
                webView.NavigationCompleted += (s, e) =>
                {
                    if (!e.IsSuccess)
                    {
                        retryTimer.Start();
                    }
                    else
                    {
                        retryTimer.Stop();
                    }
                    FindRenderHwnd();
                };

                webView.CoreWebView2.Navigate(config.Url);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "WebView2 initialization error:\n" + ex.Message + "\n\nPlease ensure Microsoft Edge WebView2 Runtime is installed.",
                    "Desktop Runner Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void ReloadPage()
        {
            if (webView != null && webView.CoreWebView2 != null)
            {
                webView.Reload();
            }
        }

        public void CloseApp()
        {
            isExiting = true;
            Cleanup();
            if (trayIcon != null)
            {
                trayIcon.Visible = false;
            }
            Application.Exit();
        }

        private void Cleanup()
        {
            try
            {
                if (hookId != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(hookId);
                    hookId = IntPtr.Zero;
                }
            }
            catch { }

            try
            {
                if (hProcExplorer != IntPtr.Zero)
                {
                    if (remoteMem != IntPtr.Zero)
                    {
                        VirtualFreeEx(hProcExplorer, remoteMem, 0, 0x8000 /* MEM_RELEASE */);
                        remoteMem = IntPtr.Zero;
                    }
                    CloseHandle(hProcExplorer);
                    hProcExplorer = IntPtr.Zero;
                }
            }
            catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Cleanup();

                if (retryTimer != null)
                {
                    retryTimer.Stop();
                    retryTimer.Dispose();
                }
                if (searchRenderTimer != null)
                {
                    searchRenderTimer.Stop();
                    searchRenderTimer.Dispose();
                }
                if (trayIcon != null)
                {
                    trayIcon.Visible = false;
                    trayIcon.Dispose();
                }
                if (trayMenu != null)
                {
                    trayMenu.Dispose();
                }
                if (webView != null)
                {
                    webView.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}
