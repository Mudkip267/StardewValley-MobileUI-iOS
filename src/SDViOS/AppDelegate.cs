using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using AVFoundation;
using Foundation;
using UIKit;
using SDViOS.Diagnostics;
using SDViOS.Loader;

namespace SDViOS
{
    [Register("AppDelegate")]
    public class AppDelegate : UIApplicationDelegate
    {
        public override UIWindow? Window { get; set; }

        public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
        {
            try
            {
                ConfigureAudioSession();

                NSNotificationCenter.DefaultCenter.AddObserver(
                    new NSString("UISceneWillConnectNotification"),
                    _ => UIApplication.SharedApplication.BeginInvokeOnMainThread(GameHost.LinkGameWindowToScene)
                );
                NSNotificationCenter.DefaultCenter.AddObserver(
                    new NSString("UISceneDidActivateNotification"),
                    _ => UIApplication.SharedApplication.BeginInvokeOnMainThread(GameHost.LinkGameWindowToScene)
                );
                NSNotificationCenter.DefaultCenter.AddObserver(
                    UIApplication.DidBecomeActiveNotification,
                    _ => UIApplication.SharedApplication.BeginInvokeOnMainThread(() =>
                    {
                        ConfigureAudioSession();
                        GameHost.LinkGameWindowToScene();
                    })
                );

                NSNotificationCenter.DefaultCenter.AddObserver(UIApplication.WillResignActiveNotification, _ => EngineLogger.Flush());
                NSNotificationCenter.DefaultCenter.AddObserver(UIApplication.DidEnterBackgroundNotification, _ => GameHost.OnDidEnterBackground());
                NSNotificationCenter.DefaultCenter.AddObserver(UIApplication.WillTerminateNotification, _ => GameHost.OnDidEnterBackground());

                GameHost.InitializeFileSystem();
                EngineLogger.Log("[AppDelegate] FinishedLaunching: Checking game files...");

                if (GameHost.TryFindGameBinary(out string sdvPath, out _))
                {
                    EngineLogger.Log($"Game files located at: {sdvPath}. Scheduling launch on main runloop...");
                    UIApplication.SharedApplication.BeginInvokeOnMainThread(() =>
                    {
                        try
                        {
                            GameHost.Launch(Array.Empty<string>());
                        }
                        catch (Exception ex)
                        {
                            EngineLogger.LogFatal("GameHost Launch on MainThread", ex);
                            ShowErrorScreen(ex);
                        }
                    });
                }
                else
                {
                    EngineLogger.LogWarning("Game files missing. Displaying setup interface.");
                    ShowMissingFilesScreen();
                }
            }
            catch (Exception ex)
            {
                EngineLogger.LogFatal("AppDelegate FinishedLaunching", ex);
                ShowErrorScreen(ex);
            }

            return true;
        }

        private void ShowMissingFilesScreen()
        {
            var scene = GameHost.GetActiveWindowScene();
            Window = scene != null ? new UIWindow(scene) : new UIWindow(UIScreen.MainScreen.Bounds);
            if (scene != null && Window.WindowScene == null)
            {
                Window.WindowScene = scene;
            }

            var vc = new UIViewController();
            vc.View!.BackgroundColor = UIColor.FromRGB(24, 30, 42);

            nfloat width = Window.Bounds.Width;
            nfloat height = Window.Bounds.Height;

            var labelTitle = new UILabel(new CoreGraphics.CGRect(20, 36, width - 40, 38))
            {
                Text = "Stardew Mobile (PC 1.6 + SMAPI)",
                TextColor = UIColor.White,
                Font = UIFont.BoldSystemFontOfSize(22),
                TextAlignment = UITextAlignment.Center
            };

            var labelStatus = new UILabel(new CoreGraphics.CGRect(30, 82, width - 60, 105))
            {
                Text = "Chưa tìm thấy dữ liệu Stardew Valley PC 1.6.15.\n\nBấm nút bên dưới để nhập file .ZIP chứa thư mục game PC (kèm SMAPI), hoặc chép trực tiếp vào Files > On My iPhone > Stardew Mobile.",
                TextColor = UIColor.FromRGB(220, 220, 220),
                Font = UIFont.SystemFontOfSize(14),
                Lines = 0,
                TextAlignment = UITextAlignment.Center
            };

            var btnImportZip = new UIButton(UIButtonType.System)
            {
                Frame = new CoreGraphics.CGRect((width - 260) / 2, 198, 260, 46),
                BackgroundColor = UIColor.FromRGB(37, 99, 235),
                Layer = { CornerRadius = 10 }
            };
            btnImportZip.SetTitle("Nhập gói dữ liệu game (.ZIP)", UIControlState.Normal);
            btnImportZip.SetTitleColor(UIColor.White, UIControlState.Normal);
            btnImportZip.TitleLabel.Font = UIFont.BoldSystemFontOfSize(16);

            var btnReload = new UIButton(UIButtonType.System)
            {
                Frame = new CoreGraphics.CGRect((width - 260) / 2, 256, 260, 44),
                BackgroundColor = UIColor.FromRGB(46, 139, 87),
                Layer = { CornerRadius = 10 }
            };
            btnReload.SetTitle("Kiểm tra & Vào game", UIControlState.Normal);
            btnReload.SetTitleColor(UIColor.White, UIControlState.Normal);
            btnReload.TitleLabel.Font = UIFont.BoldSystemFontOfSize(15);

            btnImportZip.TouchUpInside += (s, e) =>
            {
                try
                {
                    var picker = new UIDocumentPickerViewController(new[] { "public.zip-archive", "com.pkware.zip-archive" }, UIDocumentPickerMode.Import);
                    picker.AllowsMultipleSelection = false;
                    picker.DidPickDocumentAtUrls += (ps, pe) =>
                    {
                        var urls = pe.Urls;
                        if (urls == null || urls.Length == 0) return;
                        var url = urls[0];
                        btnImportZip.Enabled = false;
                        btnReload.Enabled = false;
                        labelStatus.Text = "Đang giải nén dữ liệu Stardew Valley (.ZIP)...\nVui lòng giữ màn hình sáng (khoảng 20–40 giây).";

                        Task.Run(() =>
                        {
                            bool scoped = false;
                            try
                            {
                                scoped = url.StartAccessingSecurityScopedResource();
                                string zipPath = url.Path ?? string.Empty;
                                ExtractGameZipToDocuments(zipPath, GameHost.DocumentsDir);
                                InvokeOnMainThread(() =>
                                {
                                    GameHost.InitializeFileSystem();
                                    if (GameHost.TryFindGameBinary(out _, out _))
                                    {
                                        labelStatus.Text = "Nhập dữ liệu thành công! Đang khởi động game...";
                                        GameHost.Launch(Array.Empty<string>());
                                    }
                                    else
                                    {
                                        labelStatus.Text = "Đã giải nén xong nhưng vẫn chưa thấy Stardew Valley.dll.\nHãy chắc chắn file .ZIP chứa Stardew Valley.dll và thư mục Content.";
                                        btnImportZip.Enabled = true;
                                        btnReload.Enabled = true;
                                    }
                                });
                            }
                            catch (Exception ex)
                            {
                                EngineLogger.LogError($"[ImportZip] Error: {ex}");
                                InvokeOnMainThread(() =>
                                {
                                    labelStatus.Text = $"Lỗi giải nén .ZIP: {ex.Message}";
                                    btnImportZip.Enabled = true;
                                    btnReload.Enabled = true;
                                });
                            }
                            finally
                            {
                                if (scoped) url.StopAccessingSecurityScopedResource();
                            }
                        });
                    };
                    vc.PresentViewController(picker, true, null);
                }
                catch (Exception ex)
                {
                    labelStatus.Text = $"Không mở được trình chọn tệp: {ex.Message}";
                }
            };

            btnReload.TouchUpInside += (s, e) =>
            {
                GameHost.InitializeFileSystem();
                if (GameHost.TryFindGameBinary(out _, out _))
                {
                    GameHost.Launch(Array.Empty<string>());
                }
                else
                {
                    var alert = UIAlertController.Create("Chưa thấy dữ liệu game", "Không tìm thấy Stardew Valley.dll trong thư mục Documents của app.", UIAlertControllerStyle.Alert);
                    alert.AddAction(UIAlertAction.Create("OK", UIAlertActionStyle.Default, null));
                    vc.PresentViewController(alert, true, null);
                }
            };

            vc.View.AddSubviews(labelTitle, labelStatus, btnImportZip, btnReload);
            Window.RootViewController = vc;
            Window.Hidden = false;
            Window.MakeKeyAndVisible();
        }

        private static void ExtractGameZipToDocuments(string zipPath, string docsDir)
        {
            using var archive = ZipFile.OpenRead(zipPath);
            string stripPrefix = string.Empty;
            foreach (var entry in archive.Entries)
            {
                string norm = entry.FullName.Replace('\\', '/');
                if (norm.EndsWith("/Stardew Valley.dll", StringComparison.OrdinalIgnoreCase))
                {
                    stripPrefix = norm.Substring(0, norm.Length - "Stardew Valley.dll".Length);
                    break;
                }
                if (norm.Equals("Stardew Valley.dll", StringComparison.OrdinalIgnoreCase))
                {
                    stripPrefix = string.Empty;
                    break;
                }
            }

            foreach (var entry in archive.Entries)
            {
                string norm = entry.FullName.Replace('\\', '/');
                if (string.IsNullOrEmpty(norm) || norm.EndsWith("/")) continue;
                if (norm.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase)) continue;

                if (!string.IsNullOrEmpty(stripPrefix) && norm.StartsWith(stripPrefix, StringComparison.OrdinalIgnoreCase))
                    norm = norm.Substring(stripPrefix.Length);

                // Never overwrite our built-in Mobile UI touch controls mod!
                if (norm.StartsWith("Mods/SDViOSTouchControls/", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (norm.Equals("System.Private.CoreLib.dll", StringComparison.OrdinalIgnoreCase))
                    continue;

                string destPath = Path.Combine(docsDir, norm.Replace('/', Path.DirectorySeparatorChar));
                string? destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);
                entry.ExtractToFile(destPath, overwrite: true);
            }
        }

        private void ShowErrorScreen(Exception ex)
        {
            InvokeOnMainThread(() =>
            {
                var scene = GameHost.GetActiveWindowScene();
                Window = scene != null ? new UIWindow(scene) : new UIWindow(UIScreen.MainScreen.Bounds);
                if (scene != null && Window.WindowScene == null)
                {
                    Window.WindowScene = scene;
                }

                Window.WindowLevel = UIWindowLevel.Alert + 1000;
                var vc = new UIViewController();
                vc.View!.BackgroundColor = UIColor.FromRGB(45, 12, 12);

                var realEx = ex;
                while (realEx.InnerException != null)
                {
                    realEx = realEx.InnerException;
                }

                nfloat width = Window.Bounds.Width;
                nfloat height = Window.Bounds.Height;

                var labelTitle = new UILabel(new CoreGraphics.CGRect(20, 50, width - 40, 30))
                {
                    Text = "CRASH REPORT",
                    TextColor = UIColor.Red,
                    Font = UIFont.BoldSystemFontOfSize(20),
                    TextAlignment = UITextAlignment.Center
                };

                var tv = new UITextView(new CoreGraphics.CGRect(20, 90, width - 40, height - 110))
                {
                    Text = $"Error: {realEx.GetType().Name}\nMessage: {realEx.Message}\n\n--- Call Stack ---\n{realEx.StackTrace}\n\n--- Full Details ---\n{ex}",
                    TextColor = UIColor.White,
                    Font = UIFont.SystemFontOfSize(12),
                    Editable = false,
                    BackgroundColor = UIColor.FromRGB(20, 5, 5)
                };

                vc.View.AddSubviews(labelTitle, tv);
                Window.RootViewController = vc;
                Window.Hidden = false;
                Window.MakeKeyAndVisible();
            });
        }

        public override void OnActivated(UIApplication application)
        {
            try
            {
                EngineLogger.Log("[AppDelegate] OnActivated");
                GameHost.LinkGameWindowToScene();
            }
            catch (Exception ex)
            {
                EngineLogger.LogError($"[AppDelegate] OnActivated error: {ex}");
            }
        }

        public override void DidEnterBackground(UIApplication application)
        {
            EngineLogger.Log("[AppDelegate] DidEnterBackground: pausing display link and flushing logs.");
            GameHost.OnDidEnterBackground();
        }

        public override void WillEnterForeground(UIApplication application)
        {
            try
            {
                EngineLogger.Log("[AppDelegate] WillEnterForeground: resuming display link.");
                ConfigureAudioSession();
                GameHost.OnWillEnterForeground();
            }
            catch (Exception ex)
            {
                EngineLogger.LogError($"[AppDelegate] WillEnterForeground error: {ex}");
            }
        }

        public override void ReceiveMemoryWarning(UIApplication application)
        {
            try
            {
                long beforeMb = GC.GetTotalMemory(false) / (1024 * 1024);
                System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                long afterMb = GC.GetTotalMemory(false) / (1024 * 1024);
                EngineLogger.LogWarning($"[AppDelegate] iOS ReceiveMemoryWarning! Forced LOH compacting GC: managed {beforeMb} MB -> {afterMb} MB");
            }
            catch (Exception ex)
            {
                EngineLogger.LogWarning($"[AppDelegate] ReceiveMemoryWarning GC error: {ex.Message}");
            }
        }

        public override void WillTerminate(UIApplication application)
        {
            EngineLogger.Log("[AppDelegate] WillTerminate");
            GameHost.OnDidEnterBackground();
        }

        public static void ConfigureAudioSession()
        {
            try
            {
                var session = AVAudioSession.SharedInstance();
                session.SetCategory(AVAudioSessionCategory.Playback, AVAudioSessionCategoryOptions.MixWithOthers);
                var err = session.SetActive(true);
                if (err != null)
                {
                    EngineLogger.LogWarning($"[AppDelegate] AVAudioSession.SetActive warning: {err.LocalizedDescription}");
                }
                else
                {
                    EngineLogger.Log("[AppDelegate] AVAudioSession configured successfully (Playback, MixWithOthers).");
                }
            }
            catch (Exception ex)
            {
                EngineLogger.LogWarning($"[AppDelegate] Error configuring AVAudioSession: {ex.Message}");
            }
        }
    }
}
