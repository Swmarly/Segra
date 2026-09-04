using Serilog;
using Velopack;
using Photino.NET;
using System.IO.Pipes;
using Segra.Backend.Api;
using Photino.NET.Server;
using Segra.Backend.Core;
using System.Diagnostics;
using System.Drawing;
using Segra.Backend.Shared;
using Segra.Backend.Platform;
using Segra.Backend.Recorder;
using Segra.Backend.Media;
using Segra.Backend.Core.Models;
using Segra.Backend.Windows.Storage;
using System.Reflection;
using System.Runtime.InteropServices;
#if WINDOWS
using Segra.Backend.Windows.GameMode;
using Segra.Backend.Windows.Power;
using Segra.Backend.Windows.WebView2;
#endif

namespace Segra.Backend.App
{
    class Program
    {
#if WINDOWS
        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        static extern uint GetDpiForSystem();

        [DllImport("user32.dll")]
        static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

        [DllImport("user32.dll")]
        static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        static extern uint GetCurrentThreadId();

        const int SW_HIDE = 0;
        const int SW_RESTORE = 9;
        const int SM_CXFULLSCREEN = 16;
        const int SM_CYFULLSCREEN = 17;
        const int GWL_STYLE = -16;
        const int GWL_EXSTYLE = -20;
        const int WS_OVERLAPPEDWINDOW = 0x00CF0000;
        const int WS_CAPTION = 0x00C00000;
        const int WS_THICKFRAME = 0x00040000;
        const int WS_MINIMIZEBOX = 0x00020000;
        const int WS_MAXIMIZEBOX = 0x00010000;
        const int WS_SYSMENU = 0x00080000;
        const int WS_EX_DLGMODALFRAME = 0x00000001;
        const int WS_EX_CLIENTEDGE = 0x00000200;
        const int WS_EX_STATICEDGE = 0x00020000;
        const uint MONITOR_DEFAULTTONEAREST = 2;
        const uint SWP_FRAMECHANGED = 0x0020;
        const uint SWP_SHOWWINDOW = 0x0040;
        static readonly IntPtr HWND_TOPMOST = new(-1);
        static readonly IntPtr HWND_NOTOPMOST = new(-2);
#endif
        public static bool IsFirstRun { get; private set; } = false;
        private static readonly AutoResetEvent ShowWindowEvent = new(false);
        public static bool hasLoadedInitialSettings = false;
        public static PhotinoWindow? Window { get; private set; }
        private static readonly string LogFilePath =
          Segra.Backend.Shared.PathUtils.Normalize(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Segra", "logs.log"));
        private const string PipeName = "Segra_SingleInstance";
        private static Mutex? singleInstanceMutex;
        private static Thread? pipeServerThread;
        private static string? appUrl;
        private const long maxFileSizeBytes = 10 * 1024 * 1024; // 10MB
        private const long trimTargetBytes = 8 * 1024 * 1024; // trim down to 8MB when limit is hit
        private const string LogOutputTemplate =
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

        [STAThread]
        static void Main(string[] args)
        {
            PlatformServices.Initialize();

#if WINDOWS
            // Set process DPI aware to ensure we capture at physical resolution
            SetProcessDPIAware();
#else
            // Re-exec once with LD_LIBRARY_PATH set so libobs is loadable (never returns on first launch).
            Segra.Backend.Platform.Linux.LinuxObsRuntime.ConfigureAndReexecIfNeeded();
#endif

            // Pin the working directory to the app directory so relative-path lookups
            // (OBS modules, bundled ffmpeg.exe) resolve regardless of how Segra was launched.
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);

            // In debug mode, kill any existing instances before starting
#if DEBUG
            try
            {
                var currentProcess = Process.GetCurrentProcess();
                var existingProcesses = Process.GetProcessesByName(currentProcess.ProcessName)
                    .Where(p => p.Id != currentProcess.Id);

                foreach (var process in existingProcesses)
                {
                    Console.WriteLine($"[DEBUG] Killing existing instance: PID {process.Id}");
                    process.Kill();
                    process.WaitForExit(3000); // Wait up to 3 seconds for graceful exit
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG] Failed to kill existing instance: {ex.Message}");
            }
#endif

            // Try to create a named mutex - this will fail if another instance exists
            singleInstanceMutex = new Mutex(true, "SegraApplicationMutex", out bool createdNew);

            if (!createdNew)
            {
                // Another instance exists, send a message to it via named pipe
                try
                {
                    using (var pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                    {
                        pipeClient.Connect(3000);

                        using (var writer = new StreamWriter(pipeClient))
                        {
                            writer.WriteLine("SHOW_WINDOW");
                            writer.Flush();
                        }
                    }

                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to communicate with existing instance: {ex.Message}");
                }
            }

            StartNamedPipeServer();

            var logDirectory = Path.GetDirectoryName(LogFilePath);
            if (logDirectory != null && !Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            ConfigureLogging();

            VelopackApp.Build()
                .OnBeforeUpdateFastCallback((v) =>
                {
                    if (UpdateService.UpdateManager == null)
                    {
                        Log.Error("UpdateManager is null");
                        return;
                    }
                    var currentVersion = UpdateService.UpdateManager.CurrentVersion;
                    if (currentVersion == null)
                    {
                        Log.Error("Current version is null");
                        return;
                    }
                    Log.Information($"Updating from version {currentVersion} to {v}");
                    File.WriteAllText(Path.Combine(Path.GetTempPath(), "segra.tmp"), currentVersion.ToString());
                })
                .OnAfterUpdateFastCallback((v) =>
                {
                    string previousVersionPath = Path.Combine(Path.GetTempPath(), "segra.tmp");
                    if (File.Exists(previousVersionPath))
                    {
                        string previousVersion = File.ReadAllText(previousVersionPath);
                        Log.Information($"Updated from version {previousVersion} to {v}");
                        Task.Run(async () =>
                        {
                            await Task.Delay(5000);
                            _ = MessageService.SendFrontendMessage("ShowReleaseNotes", previousVersion);
                        });
                        File.Delete(previousVersionPath);
                    }
                })
                .OnFirstRun((v) =>
                {
                    Log.Information($"First run of Segra {v}");
                })
                .Run();

            try
            {
                Log.Information("Application starting up...");

#if WINDOWS
                WebView2RuntimeService.LogRuntimeVersion();
#endif

                // VS Code sets SEGRA_VSCODE=1 via launch.json; Visual Studio does not.
                // In VS Code the Vite dev server runs separately, so PhotinoServer is not needed
                // and its RunAsync() would otherwise open a spurious browser tab.
                bool IsVSCodeDebug = Environment.GetEnvironmentVariable("SEGRA_VSCODE") == "1";
                bool IsDebugMode = Debugger.IsAttached;

                string baseUrl = string.Empty;
                if (!IsVSCodeDebug)
                {
                    PhotinoServer
                        .CreateStaticFileServer(args, startPort: 44040, portRange: 100, webRootFolder: "wwwroot", out baseUrl)
                        .RunAsync();
                }

                string appBuildStamp = File.GetLastWriteTimeUtc(typeof(Program).Assembly.Location).Ticks.ToString();
                // Version-stamped URL: WebKitGTK's disk cache persists across app updates and the
                // static server sends no cache headers, so a bare /index.html can keep rendering
                // the previous build's frontend until a manual refresh.
                string? appVersion = Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                string frontendCacheKey = $"{appVersion ?? "0"}-{appBuildStamp}";
                appUrl = IsDebugMode ? "http://localhost:2882" : $"{baseUrl}/index.html?v={Uri.EscapeDataString(frontendCacheKey)}";

                if (IsDebugMode)
                {
                    Task.Run(() =>
                    {
                        var startInfo = new ProcessStartInfo
                        {
#if WINDOWS
                            FileName = "cmd.exe",
                            Arguments = "/c npm run dev",
#else
                            FileName = "npm",
                            Arguments = "run dev",
#endif
                            WorkingDirectory = Path.Join(GetSolutionPath(), @"Frontend")
                        };

                        using (HttpClient client = new())
                        {
                            client.DefaultRequestHeaders.ExpectContinue = false;
                            try
                            {
                                // Set a short timeout since we're just checking if the server is running
                                client.Timeout = TimeSpan.FromSeconds(1);
                                var response = client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "http://localhost:2882/index.html")).Result;
                            }
                            catch (Exception)
                            {
                                Process.Start(startInfo);
                            }
                        }
                    });
                }

                Log.Information("Serving React app at {AppUrl}", appUrl);

                Task.Run(() =>
                {
                    ContentServer.StartServer(ContentServer.Prefix);
                });

                IsFirstRun = !SettingsService.LoadSettings();
                hasLoadedInitialSettings = true;
                AppState.Instance.Initialize();
                SettingsService.SaveSettings();
                if (IsFirstRun)
                {
                    _ = SettingsService.LoadContentFromFolderIntoState(true);
                    PlatformServices.Startup.SetStartupStatus(true);
#if WINDOWS
                    Settings.Instance.DisableWindowsGameMode = true;
#endif
                    AppState.Instance.GpuVendor = GeneralUtils.DetectGpuVendor();
                    SettingsService.SelectDefaultDevices();
                    _ = PresetsService.ApplyVideoPreset("high");
                    _ = PresetsService.ApplyClipPreset("standard");
                }

                // Ensure content folder exists
                if (!Directory.Exists(Settings.Instance.ContentFolder))
                {
                    try
                    {
                        Directory.CreateDirectory(Settings.Instance.ContentFolder);
                    }
                    catch (Exception ex)
                    {
                        // Saved folder is unreachable (e.g. a drive that's no longer mounted);
                        // fall back to the default so the app can still start.
                        Log.Error(ex, $"Content folder '{Settings.Instance.ContentFolder}' is not accessible, falling back to default");
                        var unreachableFolder = Settings.Instance.ContentFolder;
                        Settings.Instance.ContentFolder = Shared.PathUtils.Normalize(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Segra"));
                        Directory.CreateDirectory(Settings.Instance.ContentFolder);
                        SettingsService.SaveSettings();
                        _ = Task.Run(() => MessageService.ShowModal(
                            "Recording folder unavailable",
                            $"The recording folder '{unreachableFolder}' could not be accessed. Segra will use '{Settings.Instance.ContentFolder}' instead. You can change it in Settings.",
                            "warning"));
                    }
                }

                if (!Directory.Exists(Settings.Instance.CacheFolder))
                {
                    try
                    {
                        Directory.CreateDirectory(Settings.Instance.CacheFolder);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, $"Cache folder '{Settings.Instance.CacheFolder}' is not accessible, falling back to default");
                        var unreachableCacheFolder = Settings.Instance.CacheFolder;
                        Settings.Instance.CacheFolder = Shared.PathUtils.Normalize(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Segra"));
                        Directory.CreateDirectory(Settings.Instance.CacheFolder);
                        SettingsService.SaveSettings();
                        _ = Task.Run(() => MessageService.ShowModal(
                            "Cache folder unavailable",
                            $"The cache folder '{unreachableCacheFolder}' could not be accessed. Segra will use '{Settings.Instance.CacheFolder}' instead. You can change it in Settings.",
                            "warning"));
                    }
                }

                // Run data migrations
                Task.Run(MigrationService.RunMigrations);

                // Start WebSocket and Load Settings
                Task.Run(MessageService.StartWebsocket);
                Task.Run(StorageService.EnsureStorageBelowLimit);

                // Check for updates
                Task.Run(() => UpdateService.UpdateAppIfNecessary(forceCheck: true));

                // Check if application was launched from startup. Only minimize to tray when the
                // user has chosen the Minimized startup window mode; otherwise open normally.
                bool startMinimized = IsLaunchedFromStartup() &&
                    Settings.Instance.StartupWindowMode == StartupWindowMode.Minimized;
                Log.Information($"Starting application{(startMinimized ? " minimized from startup" : "")}");

                // Tray icon (WinForms NotifyIcon on Windows; no-op on Linux)
                PlatformServices.Tray.Initialize(
                    onOpen: () => _ = ShowApplicationWindow(),
                    onResetWindowSize: ResetWindowSize,
                    onExit: () => { Shutdown(); Environment.Exit(0); },
                    isWindowOpen: () => Window != null && !Window.Minimized);

#if WINDOWS
                // Start monitoring system power state changes (sleep/wake)
                Task.Run(PowerModeMonitor.StartMonitoring);

                // Ensure Windows Game Mode is off when the user has opted in (no-op otherwise).
                Task.Run(GameModeService.EnforceDisabledIfEnabled);

                // Run the OBS Initializer in a separate thread and application to make sure someting on the main thread doesn't block
                // (KeybindCaptureService.Start() is called from OBSService.InitializeAsync once OBS is
                // ready, since hotkeys register through OBS's own hotkey system.)
                // OBSWindow hosts the Win32 message pump the graphics-hook game_capture needs.
                Task.Run(() => Application.Run(new OBSWindow()));
#else
                // Linux libobs runs headless (no message pump needed); initialize OBS directly.
                Task.Run(() => OBSService.InitializeAsync());
#endif

                if (!startMinimized)
                {
                    LoadFrontend();
                }

                // Wait for show window events
                while (true)
                {
                    int signalIndex = WaitHandle.WaitAny([ShowWindowEvent]);
                    Log.Information($"Signal received: {signalIndex}");
                    if (signalIndex == 0)
                    {
                        Log.Information("Show window event triggered");
                        ShowApplicationWindow().GetAwaiter().GetResult();
                        Log.Information("Show window event completed");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application terminated unexpectedly.");
            }
            finally
            {
                Shutdown();
            }
        }

        public static void ConfigureLogging()
        {
            PurgeOldLogs();

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Debug()
                //.WriteTo.Debug(restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Warning) // Remove restricted minimum level to show all logs but increase lag while debugging
                .WriteTo.Sink(new TrimmingFileSink(LogFilePath, maxFileSizeBytes, trimTargetBytes, LogOutputTemplate))
                .CreateLogger();
        }

        private static Size? _windowSizeBeforeFullscreen;
        private static Point? _windowLocationBeforeFullscreen;
        private static bool _wasMaximizedBeforeFullscreen;
#if WINDOWS
        private static bool _wasTopMostBeforeFullscreen;
        private static int? _windowStyleBeforeFullscreen;
        private static int? _windowExStyleBeforeFullscreen;
        private static RECT? _windowRectBeforeFullscreen;

        [StructLayout(LayoutKind.Sequential)]
        struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public readonly int Width => Right - Left;
            public readonly int Height => Bottom - Top;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }
#endif
        private static Point? _lastNormalLocation;
        private static Size? _lastNormalSize;

        public static void SetFullscreen(bool enabled)
        {
            try
            {
                if (Window == null) return;
#if WINDOWS
                IntPtr hWnd = Window.WindowHandle;
                if (hWnd == IntPtr.Zero) return;

                if (enabled)
                {
                    _wasMaximizedBeforeFullscreen = Window.Maximized;
                    _wasTopMostBeforeFullscreen = Window.Topmost;
                    _windowSizeBeforeFullscreen = Window.Size;
                    _windowLocationBeforeFullscreen = Window.Location;
                    _windowStyleBeforeFullscreen = GetWindowLong(hWnd, GWL_STYLE);
                    _windowExStyleBeforeFullscreen = GetWindowLong(hWnd, GWL_EXSTYLE);
                    if (GetWindowRect(hWnd, out var rect))
                    {
                        _windowRectBeforeFullscreen = rect;
                    }

                    var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                    IntPtr monitor = MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST);
                    if (!GetMonitorInfo(monitor, ref monitorInfo))
                    {
                        monitorInfo.rcMonitor = new RECT
                        {
                            Left = 0,
                            Top = 0,
                            Right = GetSystemMetrics(SM_CXFULLSCREEN),
                            Bottom = GetSystemMetrics(SM_CYFULLSCREEN)
                        };
                    }

                    int style = _windowStyleBeforeFullscreen.Value
                        & ~WS_OVERLAPPEDWINDOW
                        & ~WS_CAPTION
                        & ~WS_THICKFRAME
                        & ~WS_MINIMIZEBOX
                        & ~WS_MAXIMIZEBOX
                        & ~WS_SYSMENU;
                    int exStyle = _windowExStyleBeforeFullscreen.Value
                        & ~WS_EX_DLGMODALFRAME
                        & ~WS_EX_CLIENTEDGE
                        & ~WS_EX_STATICEDGE;

                    SetWindowLong(hWnd, GWL_STYLE, style);
                    SetWindowLong(hWnd, GWL_EXSTYLE, exStyle);
                    SetWindowPos(
                        hWnd,
                        HWND_TOPMOST,
                        monitorInfo.rcMonitor.Left,
                        monitorInfo.rcMonitor.Top,
                        monitorInfo.rcMonitor.Width,
                        monitorInfo.rcMonitor.Height,
                        SWP_FRAMECHANGED | SWP_SHOWWINDOW);
                }
                else
                {
                    if (_windowStyleBeforeFullscreen.HasValue)
                    {
                        SetWindowLong(hWnd, GWL_STYLE, _windowStyleBeforeFullscreen.Value);
                    }
                    if (_windowExStyleBeforeFullscreen.HasValue)
                    {
                        SetWindowLong(hWnd, GWL_EXSTYLE, _windowExStyleBeforeFullscreen.Value);
                    }

                    if (_windowRectBeforeFullscreen.HasValue)
                    {
                        var rect = _windowRectBeforeFullscreen.Value;
                        SetWindowPos(
                            hWnd,
                            _wasTopMostBeforeFullscreen ? HWND_TOPMOST : HWND_NOTOPMOST,
                            rect.Left,
                            rect.Top,
                            rect.Width,
                            rect.Height,
                            SWP_FRAMECHANGED | SWP_SHOWWINDOW);
                    }

                    if (_wasMaximizedBeforeFullscreen)
                    {
                        Window.SetMaximized(true);
                        return;
                    }
                    else if (_windowSizeBeforeFullscreen.HasValue && _windowLocationBeforeFullscreen.HasValue)
                    {
                        // Was not maximized, restore size and position
                        Window.SetMaximized(false);
                        Window.SetSize(_windowSizeBeforeFullscreen.Value);
                        Window.SetLocation(_windowLocationBeforeFullscreen.Value);
                    }
                    else
                    {
                        Window.SetMaximized(false);
                    }
                }
#else
                if (enabled)
                {
                    _wasMaximizedBeforeFullscreen = Window.Maximized;
                    _windowSizeBeforeFullscreen = Window.Size;
                    _windowLocationBeforeFullscreen = Window.Location;
                    Window.SetMaximized(true);
                }
                else if (!_wasMaximizedBeforeFullscreen)
                {
                    Window.SetMaximized(false);
                    if (_windowSizeBeforeFullscreen.HasValue && _windowLocationBeforeFullscreen.HasValue)
                    {
                        Window.SetSize(_windowSizeBeforeFullscreen.Value);
                        Window.SetLocation(_windowLocationBeforeFullscreen.Value);
                    }
                }
#endif
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error setting fullscreen state");
            }
        }

        private static void Shutdown()
        {
            Log.Information("Application shutting down.");
            NativePlaybackAudioService.Stop();

            SaveWindowState();

            // Stop any active recording first so OBS finalizes the file cleanly. Task.Run + block keeps
            // the awaits off the tray thread, whose WinForms SynchronizationContext would otherwise deadlock.
            if (AppState.Instance.Recording != null || AppState.Instance.PreRecording != null)
            {
                Log.Information("Active recording detected during shutdown; stopping it before exit.");
                try
                {
                    Task.Run(() => OBSService.StopRecording()).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error stopping recording during shutdown");
                }
            }

            // Shutdown OBS if it was initialized
            OBSService.Shutdown();

            Log.CloseAndFlush(); // Ensure all logs are written before the application exits

            // Release the mutex when closing (only if we own it)
            if (singleInstanceMutex != null)
            {
                try
                {
                    singleInstanceMutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // Mutex was not owned by this thread, which is fine
                    // This can happen when exiting from the tray icon thread
                }
                finally
                {
                    singleInstanceMutex.Dispose();
                }
            }
        }

        private static void PurgeOldLogs()
        {
            try
            {
                var logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Segra");

                if (!Directory.Exists(logDirectory))
                    return;

                var logFiles = Directory.GetFiles(logDirectory, "*.log");

                if (logFiles.Length == 0)
                    return;

                var logFilePath = logFiles[0];
                var fileInfo = new FileInfo(logFilePath);

                if (!fileInfo.Exists || fileInfo.Length <= maxFileSizeBytes)
                    return;

                var lines = File.ReadAllLines(logFilePath).ToList();
                var avgLineSize = fileInfo.Length / lines.Count;
                var linesToKeep = (int)(trimTargetBytes / avgLineSize);

                if (linesToKeep < lines.Count)
                {
                    var recentLines = lines.Skip(lines.Count - linesToKeep).ToList();
                    File.WriteAllLines(logFilePath, recentLines);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error purging logs: {ex.Message}");
            }
        }

        private static async Task BringWindowToForegroundAsync()
        {
            if (Window == null)
                return;

            Window.Invoke(() =>
            {
                Window.SetMinimized(false);
                Window.SetTopMost(true);
            });
            await Task.Delay(200);
            Window.Invoke(() => Window.SetTopMost(false));
            FocusApplicationWindow();
            Log.Information("Application window brought to foreground");
        }

        private static async Task ShowApplicationWindow()
        {
            Log.Information("Showing application window. Window is " + (Window == null ? "null" : "not null"));
            if (Window == null)
            {
                // Schedule the foreground operations with a delay before calling LoadFrontend
                _ = Task.Run(async () =>
                {
                    await Task.Delay(200);
                    Log.Information("Bringing application window to foreground from scheduled task");
                    await BringWindowToForegroundAsync();
                });

                LoadFrontend();
            }
            else
            {
                Log.Information("Bringing application window to foreground. Window is not null");
                await BringWindowToForegroundAsync();
            }
        }

        public static void BringWindowToFront() => _ = ShowApplicationWindow();

        // SetForegroundWindow only works for the process owning the foreground, so borrow its input queue.
        private static void FocusApplicationWindow()
        {
#if WINDOWS
            try
            {
                IntPtr hWnd = Process.GetCurrentProcess().MainWindowHandle;
                if (hWnd == IntPtr.Zero)
                    return;

                IntPtr foreground = GetForegroundWindow();
                if (foreground == hWnd)
                    return;

                ShowWindow(hWnd, SW_RESTORE);

                uint foregroundThread = GetWindowThreadProcessId(foreground, IntPtr.Zero);
                uint currentThread = GetCurrentThreadId();
                bool attached = foregroundThread != 0 && foregroundThread != currentThread &&
                    AttachThreadInput(currentThread, foregroundThread, true);

                SetForegroundWindow(hWnd);

                if (attached)
                    AttachThreadInput(currentThread, foregroundThread, false);

                Log.Information("Application window focused");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not focus the application window");
            }
#endif
        }

        private static void HideApplicationWindow()
        {
            Window?.SetMinimized(true);

#if WINDOWS
            IntPtr hWnd = Process.GetCurrentProcess().MainWindowHandle;
            ShowWindow(hWnd, SW_HIDE); // Hides the window from the taskbar
#endif

            Log.Information("Application window hidden");
        }

        private static void LoadFrontend()
        {
            Log.Information("Loading frontend, app url is " + appUrl);

#if WINDOWS
            string iconFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
#else
            string iconFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.png");
#endif
            var windowSize = GetDefaultWindowSize();

            bool hasRestoredLocation = TryGetRestoredWindowLocation(out Point restoredLocation);

            // Restore the last window size too (older saved states have no size; keep the default then).
            var savedState = Settings.Instance.LastWindowState;
            bool restoreMaximized = false;
            if (hasRestoredLocation && savedState != null)
            {
                if (savedState.Width > 0 && savedState.Height > 0)
                {
                    windowSize = new Size(savedState.Width, savedState.Height);
                }
                restoreMaximized = savedState.Maximized;
            }

            // Initialize the PhotinoWindow
            var windowBuilder = new PhotinoWindow();
#if WINDOWS
            // Chromium/WebView2-only flags; WebKitGTK on Linux parses these natively and crashes on the
            // leading "--", so they must only be set on Windows.
            string browserArgs = "--enable-blink-features=AudioVideoTracks --disable-http-cache";

            // Without this, Chromium runs system proxy auto-detection (WPAD) on launch and the
            // webview's first request to the local server stalls ~1s waiting for it. Only skip
            // proxy support when the user has no proxy configured, since the frontend also
            // calls segra.tv directly.
            if (!HasUserConfiguredProxy())
            {
                browserArgs += " --no-proxy-server";
            }
            windowBuilder = windowBuilder.SetBrowserControlInitParameters(browserArgs);
#endif
            windowBuilder = windowBuilder
                .SetNotificationsEnabled(false) // Disabled due to it creating a second start menu entry with incorrect start path. See https://github.com/tryphotino/photino.NET/issues/85
                .SetUseOsDefaultSize(false)
                .SetIconFile(iconFile)
                .SetSize(windowSize)
                .SetResizable(true);

            // Restore the window to the monitor it was last on instead of always centering on the primary display.
            // UseOsDefaultLocation defaults to true, so it must be explicitly disabled or the native window
            // ignores SetLocation and falls back to the OS default position (same reasoning as SetUseOsDefaultSize above).
            windowBuilder = hasRestoredLocation
                ? windowBuilder.SetUseOsDefaultLocation(false).SetLocation(restoredLocation)
                : windowBuilder.Center();

            // The window maximizes on the monitor containing the restored location.
            if (restoreMaximized)
            {
                windowBuilder = windowBuilder.SetMaximized(true);
            }

            Window = windowBuilder
                .RegisterWebMessageReceivedHandler((sender, message) =>
                {
                    Window = (PhotinoWindow)sender!;
                    _ = MessageService.HandleMessage(message);
                })
                .Load(appUrl);

            Log.Information("Window variable has been set");

            // intentional space after name because of https://github.com/tryphotino/photino.NET/issues/106
            Window.SetTitle("Segra ");

            // Track the last normal (not maximized/minimized) bounds so SaveWindowState can persist
            // a sensible restore size even when the window is closed while maximized.
            Window.RegisterLocationChangedHandler((sender, location) =>
            {
                if (Window != null && !Window.Maximized && !Window.Minimized)
                {
                    _lastNormalLocation = location;
                }
            });
            Window.RegisterSizeChangedHandler((sender, size) =>
            {
                if (Window != null && !Window.Maximized && !Window.Minimized)
                {
                    _lastNormalSize = size;
                }
            });

            Window.RegisterWindowClosingHandler((sender, eventArgs) =>
            {
                if (Settings.Instance.CloseButtonAction == CloseButtonAction.Exit)
                {
                    Shutdown();
                    Environment.Exit(0);
                    return false;
                }

                SaveWindowState();
                HideApplicationWindow();
                return true;
            });

            Window.WaitForClose();
        }

        private static Size GetDefaultWindowSize()
        {
#if WINDOWS
            // Photino sizes windows in physical pixels, so scale the default size by the
            // OS display scale (e.g. 150% on 4K monitors) and clamp it to the usable screen area
            double displayScale = GetDpiForSystem() / 96.0;
            return new Size(
                Math.Min((int)(1280 * displayScale), GetSystemMetrics(SM_CXFULLSCREEN)),
                Math.Min((int)(720 * displayScale), GetSystemMetrics(SM_CYFULLSCREEN)));
#else
            // WebKitGTK handles DPI scaling itself; use a sensible default size.
            return new Size(1280, 720);
#endif
        }

        // Returns the window to its default size while keeping its position. Triggered from the tray menu.
        public static void ResetWindowSize()
        {
            // Task.Run keeps the work off the tray thread (see Shutdown for the reasoning).
            _ = Task.Run(async () =>
            {
                try
                {
                    if (Window == null)
                    {
                        // No window to resize; drop the saved size (keeping the position) so the
                        // next launch uses the default.
                        var saved = Settings.Instance.LastWindowState;
                        if (saved != null)
                        {
                            Settings.Instance.LastWindowState = new WindowState { X = saved.X, Y = saved.Y };
                            SettingsService.SaveSettings();
                        }
                        return;
                    }

                    // Show the window first so the resize is visible and not applied while minimized.
                    await ShowApplicationWindow();
                    Window.Invoke(() =>
                    {
                        Window.SetMaximized(false);
                        Window.SetSize(GetDefaultWindowSize());
                    });

                    Log.Information("Window size reset to default");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error resetting window size");
                }
            });
        }

        // Validates the saved location still lands on a currently connected monitor
        // (e.g. the second monitor wasn't unplugged), falling back to centering otherwise.
        private static bool TryGetRestoredWindowLocation(out Point location)
        {
            var saved = Settings.Instance.LastWindowState;
            if (saved != null)
            {
                var savedLocation = new Point(saved.X, saved.Y);
#if WINDOWS
                // Only restore if the saved location still lands on a connected monitor.
                if (Screen.AllScreens.Any(screen => screen.Bounds.Contains(savedLocation)))
                {
                    location = savedLocation;
                    return true;
                }
#else
                // No cross-platform multi-monitor bounds query; trust the saved location.
                location = savedLocation;
                return true;
#endif
            }

            location = default;
            return false;
        }

        private static void SaveWindowState()
        {
            if (Window == null || Window.Minimized) return;

            try
            {
                bool maximized = Window.Maximized;
                Point location = Window.Location;
                Size size = Window.Size;

                // A maximized window reports its maximized bounds, so persist the last
                // tracked normal bounds (or the previously saved ones) instead.
                if (maximized)
                {
                    var previous = Settings.Instance.LastWindowState;
                    location = _lastNormalLocation
                        ?? (previous != null ? new Point(previous.X, previous.Y) : location);
                    size = _lastNormalSize
                        ?? (previous is { Width: > 0, Height: > 0 } ? new Size(previous.Width, previous.Height) : size);
                }

                Settings.Instance.LastWindowState = new WindowState
                {
                    X = location.X,
                    Y = location.Y,
                    Width = size.Width,
                    Height = size.Height,
                    Maximized = maximized
                };

                SettingsService.SaveSettings();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error saving window state");
            }
        }

        private static void StartNamedPipeServer()
        {
            pipeServerThread = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        using (var pipeServer = new NamedPipeServerStream(PipeName, PipeDirection.In))
                        {
                            pipeServer.WaitForConnection();

                            using (var reader = new StreamReader(pipeServer))
                            {
                                string? message = reader.ReadLine();
                                if (message == "SHOW_WINDOW")
                                {
                                    if (Window != null)
                                    {
                                        Window.Invoke(() =>
                                        {
                                            Window.SetMinimized(false);
                                            Window.SetTopMost(true);
                                        });
                                        Thread.Sleep(200);
                                        Window.Invoke(() => Window.SetTopMost(false));
                                        Log.Information("Window brought to foreground directly from pipe server");
                                    }
                                    else
                                    {
                                        // Only signal the main thread to create the window if it doesn't exist
                                        ShowWindowEvent.Set();
                                        Log.Information("ShowWindowEvent set");
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (Log.Logger != null)
                        {
                            Log.Error(ex, "Error in named pipe server");
                        }
                        else
                        {
                            Console.WriteLine($"Error in named pipe server: {ex.Message}");
                        }

                        Thread.Sleep(1000);
                    }
                }
            });

            pipeServerThread.IsBackground = true;
            pipeServerThread.Start();
        }

#if WINDOWS
        // True when the user has an explicit proxy or PAC script configured in Windows.
        private static bool HasUserConfiguredProxy()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
                if (key == null) return false;

                bool proxyEnabled = key.GetValue("ProxyEnable") is int enabled && enabled != 0;
                bool hasPacUrl = !string.IsNullOrEmpty(key.GetValue("AutoConfigURL") as string);
                return proxyEnabled || hasPacUrl;
            }
            catch
            {
                return false;
            }
        }
#endif

        // Check if the application was launched from startup
        private static bool IsLaunchedFromStartup()
        {
            return Environment.GetCommandLineArgs().Contains("--from-startup");
        }

        private static string GetSolutionPath()
        {
            string currentDirectory = Directory.GetCurrentDirectory();

            string directory = currentDirectory;
            while (!string.IsNullOrEmpty(directory) && !Directory.GetFiles(directory, "*.sln").Any())
            {
                directory = Directory.GetParent(directory)?.FullName!;
            }

            if (string.IsNullOrEmpty(directory))
            {
                throw new InvalidOperationException("Solution path could not be found. Ensure you are running this application within a solution directory.");
            }

            return directory;
        }
    }
}
