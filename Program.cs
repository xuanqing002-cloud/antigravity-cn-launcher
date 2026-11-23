using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AntigravityCnTool
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static int Main(string[] args)
        {
            // Enable TLS 1.2 & TLS 1.3 for GitHub and Node.js HTTPS endpoints on .NET Framework 4.x
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)12288;
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.DefaultConnectionLimit = 16;

            CliOptions options = CliOptions.Parse(args);

            if (options.CliInstall || options.CliRestore)
            {
                return RunCli(options);
            }

            try
            {
                SetProcessDPIAware();
            }
            catch
            {
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(options));
            return 0;
        }

        private static int RunCli(CliOptions options)
        {
            object logLock = new object();
            StreamWriter logWriter = null;
            if (!string.IsNullOrEmpty(options.LogPath))
            {
                string logDir = Path.GetDirectoryName(Path.GetFullPath(options.LogPath));
                if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }
                logWriter = new StreamWriter(options.LogPath, false, new UTF8Encoding(true));
                logWriter.AutoFlush = true;
            }

            Action<string> logger = delegate(string msg)
            {
                string line = string.Format("[{0}] {1}", DateTime.Now.ToString("HH:mm:ss"), msg);
                lock (logLock)
                {
                    Console.WriteLine(line);
                    if (logWriter != null)
                    {
                        logWriter.WriteLine(line);
                    }
                }
            };

            Action<int, string> progress = delegate(int pct, string status)
            {
                // Keep CLI log readable without flooding download progress percentages
            };

            try
            {
                LocalizationService service = new LocalizationService();
                string installDir = options.InstallDir;
                if (string.IsNullOrEmpty(installDir))
                {
                    installDir = service.DetectInstallDir();
                }

                if (string.IsNullOrEmpty(installDir) || !service.IsValidInstallDir(installDir))
                {
                    logger("[错误] 未检测到有效的 Antigravity 安装目录: " + (installDir ?? "(空)"));
                    return 1;
                }

                logger("目标目录: " + installDir);
                logger("软件版本: " + service.GetAppVersion(installDir));
                logger("当前状态: " + (service.IsLocalized(installDir) ? "已汉化" : "未汉化"));

                bool ok;
                if (options.CliRestore)
                {
                    ok = service.ExecuteRestoreAsync(installDir, options.NoKill, logger, progress).GetAwaiter().GetResult();
                }
                else
                {
                    ok = service.ExecuteInstallAsync(
                        installDir,
                        options.UseTw,
                        options.BrandTitle,
                        options.NoKill,
                        !options.NoLaunch,
                        logger,
                        progress).GetAwaiter().GetResult();
                }

                logger(ok ? "[完成] 操作执行成功。" : "[失败] 操作未成功，请检查上方日志。");
                return ok ? 0 : 1;
            }
            catch (Exception ex)
            {
                logger("[异常] " + ex.Message);
                return 1;
            }
            finally
            {
                if (logWriter != null)
                {
                    logWriter.Dispose();
                }
            }
        }
    }

    internal sealed class CliOptions
    {
        public bool CliInstall;
        public bool CliRestore;
        public string InstallDir;
        public bool NoKill;
        public bool NoLaunch;
        public string BrandTitle = "english";
        public bool UseTw;
        public bool NoAuto;
        public string LogPath;

        public static CliOptions Parse(string[] args)
        {
            CliOptions opt = new CliOptions();
            if (args == null) return opt;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--cli-install")
                {
                    opt.CliInstall = true;
                }
                else if (a == "--cli-restore")
                {
                    opt.CliRestore = true;
                }
                else if (a == "--no-kill")
                {
                    opt.NoKill = true;
                }
                else if (a == "--no-launch")
                {
                    opt.NoLaunch = true;
                }
                else if (a == "--tw")
                {
                    opt.UseTw = true;
                }
                else if (a == "--no-auto")
                {
                    opt.NoAuto = true;
                }
                else if (a == "--install-dir" && i + 1 < args.Length)
                {
                    opt.InstallDir = args[++i];
                }
                else if (a.StartsWith("--install-dir="))
                {
                    opt.InstallDir = a.Substring("--install-dir=".Length);
                }
                else if (a == "--brand-title" && i + 1 < args.Length)
                {
                    opt.BrandTitle = NormalizeBrandTitle(args[++i]);
                }
                else if (a.StartsWith("--brand-title="))
                {
                    opt.BrandTitle = NormalizeBrandTitle(a.Substring("--brand-title=".Length));
                }
                else if (a == "--log" && i + 1 < args.Length)
                {
                    opt.LogPath = args[++i];
                }
                else if (a.StartsWith("--log="))
                {
                    opt.LogPath = a.Substring("--log=".Length);
                }
            }

            return opt;
        }

        private static string NormalizeBrandTitle(string val)
        {
            if (string.IsNullOrEmpty(val)) return "english";
            string lower = val.Trim().ToLowerInvariant();
            if (lower == "hidden" || lower == "hide" || lower == "none") return "hidden";
            if (lower == "translated" || lower == "chinese" || lower == "cn" || lower == "zh") return "translated";
            return "english";
        }
    }

    internal sealed class LocalizationService
    {
        public const string RepoDisplayUrl = "https://github.com/qqxpee/antigravity2-cn";
        private const string GitHubShaUrl = "https://api.github.com/repos/qqxpee/antigravity2-cn/commits/main";
        private const string GitHubZipPrimary = "https://codeload.github.com/qqxpee/antigravity2-cn/zip/refs/heads/main";
        private const string GitHubZipFallback = "https://github.com/qqxpee/antigravity2-cn/archive/refs/heads/main.zip";
        private const string NodeZipPrimary = "https://nodejs.org/dist/v22.14.0/node-v22.14.0-win-x64.zip";
        private const string NodeZipFallback = "https://registry.npmmirror.com/-/binary/node/v22.14.0/node-v22.14.0-win-x64.zip";
        private const string AsarSignature = "/* --- ANTIGRAVITY CHINESE LOCALIZATION START --- */";

        public string CacheRoot { get; private set; }
        public string RuntimeDir { get; private set; }
        public string CachedNodePath { get; private set; }
        public string RepoDir { get; private set; }
        public string RepoShaPath { get; private set; }

        public LocalizationService()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            CacheRoot = Path.Combine(localAppData, "AntigravityCN");
            RuntimeDir = Path.Combine(CacheRoot, "runtime");
            CachedNodePath = Path.Combine(RuntimeDir, "node.exe");
            RepoDir = Path.Combine(CacheRoot, "repo");
            RepoShaPath = Path.Combine(CacheRoot, "repo_sha.txt");
        }

        public bool IsValidInstallDir(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return false;
            try
            {
                string exe = Path.Combine(dir, "Antigravity.exe");
                string asar = Path.Combine(dir, "resources", "app.asar");
                return File.Exists(exe) && File.Exists(asar);
            }
            catch
            {
                return false;
            }
        }

        public string DetectInstallDir()
        {
            List<string> candidates = new List<string>();
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            if (!string.IsNullOrEmpty(localAppData))
            {
                candidates.Add(Path.Combine(localAppData, "Programs", "antigravity"));
                candidates.Add(Path.Combine(localAppData, "Programs", "Antigravity"));
            }
            if (!string.IsNullOrEmpty(programFiles))
            {
                candidates.Add(Path.Combine(programFiles, "Antigravity"));
            }
            if (!string.IsNullOrEmpty(programFilesX86))
            {
                candidates.Add(Path.Combine(programFilesX86, "Antigravity"));
            }

            foreach (string c in candidates)
            {
                if (IsValidInstallDir(c))
                {
                    return c;
                }
            }

            // Fallback: inspect Windows Uninstall registry entries
            RegistryHive[] hives = new RegistryHive[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine };
            RegistryView[] views = new RegistryView[] { RegistryView.Registry64, RegistryView.Registry32 };
            foreach (RegistryHive hive in hives)
            {
                foreach (RegistryView view in views)
                {
                    try
                    {
                        using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
                        using (RegistryKey uninstall = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall"))
                        {
                            if (uninstall == null) continue;
                            foreach (string subName in uninstall.GetSubKeyNames())
                            {
                                using (RegistryKey sub = uninstall.OpenSubKey(subName))
                                {
                                    if (sub == null) continue;
                                    string displayName = Convert.ToString(sub.GetValue("DisplayName"));
                                    if (string.IsNullOrEmpty(displayName) ||
                                        displayName.IndexOf("antigravity", StringComparison.OrdinalIgnoreCase) < 0)
                                    {
                                        continue;
                                    }

                                    string installLoc = CleanPathValue(Convert.ToString(sub.GetValue("InstallLocation")));
                                    if (IsValidInstallDir(installLoc)) return installLoc;

                                    string displayIcon = CleanPathValue(Convert.ToString(sub.GetValue("DisplayIcon")));
                                    if (!string.IsNullOrEmpty(displayIcon))
                                    {
                                        string dir = Path.GetDirectoryName(displayIcon);
                                        if (IsValidInstallDir(dir)) return dir;
                                    }

                                    string uninstallStr = CleanPathValue(Convert.ToString(sub.GetValue("UninstallString")));
                                    if (!string.IsNullOrEmpty(uninstallStr))
                                    {
                                        string dir = Path.GetDirectoryName(uninstallStr);
                                        if (IsValidInstallDir(dir)) return dir;
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return null;
        }

        private static string CleanPathValue(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            string s = raw.Trim().Trim('"');
            int commaIdx = s.IndexOf(',');
            if (commaIdx > 0)
            {
                s = s.Substring(0, commaIdx).Trim().Trim('"');
            }
            return s;
        }

        public string GetAppVersion(string installDir)
        {
            if (string.IsNullOrEmpty(installDir)) return "未知";
            try
            {
                string exePath = Path.Combine(installDir, "Antigravity.exe");
                if (File.Exists(exePath))
                {
                    FileVersionInfo info = FileVersionInfo.GetVersionInfo(exePath);
                    if (!string.IsNullOrEmpty(info.ProductVersion)) return info.ProductVersion.Trim();
                    if (!string.IsNullOrEmpty(info.FileVersion)) return info.FileVersion.Trim();
                }

                string versionFile = Path.Combine(installDir, "version");
                if (File.Exists(versionFile))
                {
                    return File.ReadAllText(versionFile, Encoding.UTF8).Trim();
                }
            }
            catch
            {
            }
            return "未知";
        }

        public bool IsLocalized(string installDir)
        {
            if (string.IsNullOrEmpty(installDir)) return false;
            string asarPath = Path.Combine(installDir, "resources", "app.asar");
            if (!File.Exists(asarPath)) return false;

            byte[] needle = Encoding.ASCII.GetBytes(AsarSignature);
            int overlap = needle.Length - 1;
            byte[] buffer = new byte[1024 * 1024 + overlap];

            try
            {
                using (FileStream fs = new FileStream(asarPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    int carry = 0;
                    int read;
                    while ((read = fs.Read(buffer, carry, buffer.Length - carry)) > 0)
                    {
                        int total = carry + read;
                        if (IndexOfBytes(buffer, total, needle) >= 0)
                        {
                            return true;
                        }
                        if (total >= overlap)
                        {
                            Buffer.BlockCopy(buffer, total - overlap, buffer, 0, overlap);
                            carry = overlap;
                        }
                        else
                        {
                            carry = total;
                        }
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private static int IndexOfBytes(byte[] haystack, int haystackLen, byte[] needle)
        {
            int limit = haystackLen - needle.Length;
            byte first = needle[0];
            for (int i = 0; i <= limit; i++)
            {
                if (haystack[i] != first) continue;
                bool match = true;
                for (int j = 1; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }

        public Task<string> EnsureNodeAsync(Action<string> log, Action<int, string> progress)
        {
            return Task.Run(delegate()
            {
                string existing = FindExistingNode();
                if (!string.IsNullOrEmpty(existing))
                {
                    log("已就绪 Node.js 运行时: " + existing);
                    return existing;
                }

                Directory.CreateDirectory(RuntimeDir);
                string zipPath = Path.Combine(CacheRoot, "node_tmp_" + Guid.NewGuid().ToString("N") + ".zip");
                string tmpExe = Path.Combine(RuntimeDir, "node.exe.tmp." + Guid.NewGuid().ToString("N"));

                try
                {
                    log("系统未安装 Node.js，正在首次下载便携版 Node.js v22 LTS (~33 MB)...");
                    progress(10, "正在下载 Node.js 运行时...");

                    try
                    {
                        DownloadFile(NodeZipPrimary, zipPath, 60000, delegate(int pct)
                        {
                            progress(10 + (int)(pct * 0.35), string.Format("正在下载 Node.js ({0}%)...", pct));
                        });
                    }
                    catch (Exception primaryEx)
                    {
                        log("官方源下载失败 (" + primaryEx.Message + ")，尝试国内镜像源...");
                        DownloadFile(NodeZipFallback, zipPath, 60000, delegate(int pct)
                        {
                            progress(10 + (int)(pct * 0.35), string.Format("正在从镜像下载 Node.js ({0}%)...", pct));
                        });
                    }

                    progress(48, "正在提取 node.exe...");
                    log("正在解压便携版 node.exe...");
                    bool extracted = false;
                    using (ZipArchive archive = ZipFile.OpenRead(zipPath))
                    {
                        foreach (ZipArchiveEntry entry in archive.Entries)
                        {
                            if (entry.Name.Equals("node.exe", StringComparison.OrdinalIgnoreCase))
                            {
                                entry.ExtractToFile(tmpExe, true);
                                extracted = true;
                                break;
                            }
                        }
                    }

                    if (!extracted || !File.Exists(tmpExe))
                    {
                        throw new InvalidOperationException("下载的 Node.js 压缩包中未找到 node.exe。");
                    }

                    if (File.Exists(CachedNodePath))
                    {
                        File.Delete(CachedNodePath);
                    }
                    File.Move(tmpExe, CachedNodePath);
                    log("Node.js 运行时已缓存: " + CachedNodePath);
                    return CachedNodePath;
                }
                finally
                {
                    TryDeleteFile(zipPath);
                    TryDeleteFile(tmpExe);
                }
            });
        }

        private string FindExistingNode()
        {
            if (File.Exists(CachedNodePath))
            {
                return CachedNodePath;
            }

            List<string> candidates = new List<string>();
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            if (!string.IsNullOrEmpty(pf)) candidates.Add(Path.Combine(pf, "nodejs", "node.exe"));
            if (!string.IsNullOrEmpty(pf86)) candidates.Add(Path.Combine(pf86, "nodejs", "node.exe"));
            if (!string.IsNullOrEmpty(local)) candidates.Add(Path.Combine(local, "Programs", "node", "node.exe"));

            string pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                string[] parts = pathEnv.Split(';');
                foreach (string p in parts)
                {
                    string trimmed = p.Trim().Trim('"');
                    if (!string.IsNullOrEmpty(trimmed))
                    {
                        candidates.Add(Path.Combine(trimmed, "node.exe"));
                    }
                }
            }

            foreach (string c in candidates)
            {
                try
                {
                    if (File.Exists(c)) return c;
                }
                catch
                {
                }
            }

            // Last check via where node
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("where", "node");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                using (Process proc = Process.Start(psi))
                {
                    string outText = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(3000);
                    if (proc.ExitCode == 0 && !string.IsNullOrEmpty(outText))
                    {
                        string[] lines = outText.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (string line in lines)
                        {
                            string candidate = line.Trim();
                            if (File.Exists(candidate) && candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            {
                                return candidate;
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        public Task<string> EnsureRepoAsync(Action<string> log, Action<int, string> progress)
        {
            return Task.Run(delegate()
            {
                Directory.CreateDirectory(CacheRoot);

                bool hasLocalRepo = IsValidRepoDir(RepoDir);
                string localSha = File.Exists(RepoShaPath) ? File.ReadAllText(RepoShaPath, Encoding.UTF8).Trim() : string.Empty;
                string remoteSha = null;

                progress(50, "正在检查 GitHub 最新汉化包...");
                log("正在查询 GitHub 最新版本 (qqxpee/antigravity2-cn)...");
                try
                {
                    remoteSha = FetchGitHubHeadSha();
                    if (!string.IsNullOrEmpty(remoteSha))
                    {
                        string shortSha = remoteSha.Length >= 7 ? remoteSha.Substring(0, 7) : remoteSha;
                        if (hasLocalRepo && string.Equals(localSha, remoteSha, StringComparison.OrdinalIgnoreCase))
                        {
                            log("已是最新 GitHub 汉化包 (提交: " + shortSha + ")，跳过下载。");
                            progress(65, "汉化包已是最新版本");
                            return RepoDir;
                        }
                        log("检测到远端最新版本: " + shortSha);
                    }
                }
                catch (Exception ex)
                {
                    log("查询 GitHub 版本信息失败 (" + ex.Message + ")，尝试直接下载或使用本地缓存...");
                }

                string zipPath = Path.Combine(CacheRoot, "repo_tmp_" + Guid.NewGuid().ToString("N") + ".zip");
                string stagingDir = Path.Combine(CacheRoot, "staging_" + Guid.NewGuid().ToString("N"));

                try
                {
                    progress(55, "正在下载 GitHub 汉化包...");
                    log("正在从 GitHub 下载最新汉化包...");

                    try
                    {
                        DownloadFile(GitHubZipPrimary, zipPath, 30000, delegate(int pct)
                        {
                            progress(55 + (int)(pct * 0.15), string.Format("正在下载 GitHub 汉化包 ({0}%)...", pct));
                        });
                    }
                    catch (Exception ex1)
                    {
                        log("codeload 下载失败 (" + ex1.Message + ")，尝试 GitHub archive 地址...");
                        DownloadFile(GitHubZipFallback, zipPath, 30000, delegate(int pct)
                        {
                            progress(55 + (int)(pct * 0.15), string.Format("正在下载 GitHub 汉化包 ({0}%)...", pct));
                        });
                    }

                    progress(72, "正在解压汉化包...");
                    Directory.CreateDirectory(stagingDir);
                    ZipFile.ExtractToDirectory(zipPath, stagingDir);

                    string extractedRoot = FindExtractedRepoRoot(stagingDir);
                    if (string.IsNullOrEmpty(extractedRoot))
                    {
                        throw new InvalidDataException("解压后的 GitHub 压缩包缺少 localization_engine.js 或 dicts 目录。");
                    }

                    ReplaceDirectorySafely(extractedRoot, RepoDir);

                    if (!string.IsNullOrEmpty(remoteSha))
                    {
                        File.WriteAllText(RepoShaPath, remoteSha, Encoding.UTF8);
                    }
                    else
                    {
                        TryDeleteFile(RepoShaPath);
                    }

                    log("GitHub 汉化包已同步至本地缓存: " + RepoDir);
                    progress(75, "汉化包已就绪");
                    return RepoDir;
                }
                catch (Exception downloadEx)
                {
                    if (hasLocalRepo)
                    {
                        log("[警告] 从 GitHub 更新失败 (" + downloadEx.Message + ")，将回退使用本地已缓存的汉化包。");
                        progress(75, "使用本地缓存汉化包");
                        return RepoDir;
                    }
                    throw;
                }
                finally
                {
                    TryDeleteFile(zipPath);
                    TryDeleteDirectory(stagingDir);
                }
            });
        }

        private static bool IsValidRepoDir(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
            return File.Exists(Path.Combine(dir, "localization_engine.js")) &&
                   Directory.Exists(Path.Combine(dir, "dicts"));
        }

        private static string FindExtractedRepoRoot(string stagingDir)
        {
            if (IsValidRepoDir(stagingDir)) return stagingDir;
            string[] subDirs = Directory.GetDirectories(stagingDir);
            foreach (string sub in subDirs)
            {
                if (IsValidRepoDir(sub)) return sub;
            }
            return null;
        }

        private static void ReplaceDirectorySafely(string sourceDir, string targetDir)
        {
            string backupDir = targetDir + ".old_" + Guid.NewGuid().ToString("N");
            bool movedOld = false;
            try
            {
                if (Directory.Exists(targetDir))
                {
                    Directory.Move(targetDir, backupDir);
                    movedOld = true;
                }
                Directory.Move(sourceDir, targetDir);
                if (movedOld)
                {
                    TryDeleteDirectory(backupDir);
                }
            }
            catch
            {
                if (!Directory.Exists(targetDir) && movedOld && Directory.Exists(backupDir))
                {
                    try { Directory.Move(backupDir, targetDir); } catch { }
                }
                throw;
            }
        }

        private static string FetchGitHubHeadSha()
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(GitHubShaUrl);
            req.Method = "GET";
            req.Timeout = 10000;
            req.ReadWriteTimeout = 10000;
            req.UserAgent = "Antigravity-CN-Tool/1.0";
            req.Accept = "application/vnd.github.sha";

            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (Stream stream = resp.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                return reader.ReadToEnd().Trim();
            }
        }

        private static void DownloadFile(string url, string destPath, int timeoutMs, Action<int> onProgress)
        {
            string tempPath = destPath + ".part";
            TryDeleteFile(tempPath);

            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.AllowAutoRedirect = true;
            req.UserAgent = "Antigravity-CN-Tool/1.0";

            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (Stream inStream = resp.GetResponseStream())
            using (FileStream outStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                long totalBytes = resp.ContentLength;
                byte[] buffer = new byte[65536];
                long downloaded = 0;
                int lastReported = -1;
                int read;
                while ((read = inStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    outStream.Write(buffer, 0, read);
                    downloaded += read;
                    if (totalBytes > 0 && onProgress != null)
                    {
                        int pct = (int)((downloaded * 100L) / totalBytes);
                        if (pct > 100) pct = 100;
                        if (pct != lastReported)
                        {
                            lastReported = pct;
                            onProgress(pct);
                        }
                    }
                }
            }

            if (File.Exists(destPath)) File.Delete(destPath);
            File.Move(tempPath, destPath);
        }

        public async Task<bool> ExecuteInstallAsync(
            string installDir,
            bool useTw,
            string brandTitle,
            bool noKill,
            bool autoLaunch,
            Action<string> log,
            Action<int, string> progress)
        {
            string nodeExe = await EnsureNodeAsync(log, progress);
            string repoDir = await EnsureRepoAsync(log, progress);

            progress(78, "正在注入中文汉化补丁...");
            List<string> args = new List<string>();
            args.Add(QuoteArg(Path.Combine(repoDir, "localization_engine.js")));
            args.Add("--brand-title");
            args.Add(string.IsNullOrEmpty(brandTitle) ? "english" : brandTitle);
            if (useTw) args.Add("--tw");
            if (noKill) args.Add("--no-kill");
            args.Add("--install-dir");
            args.Add(QuoteArg(installDir));

            int exitCode = await RunEngineProcessAsync(nodeExe, repoDir, string.Join(" ", args.ToArray()), log);
            if (exitCode != 0)
            {
                progress(100, "汉化失败");
                return false;
            }

            if (autoLaunch && !noKill)
            {
                try
                {
                    Process[] running = Process.GetProcessesByName("Antigravity");
                    if (running.Length == 0)
                    {
                        string exePath = Path.Combine(installDir, "Antigravity.exe");
                        if (File.Exists(exePath))
                        {
                            log("正在启动 Antigravity...");
                            ProcessStartInfo launchPsi = new ProcessStartInfo(exePath);
                            launchPsi.WorkingDirectory = installDir;
                            launchPsi.UseShellExecute = true;
                            Process.Start(launchPsi);
                        }
                    }
                }
                catch (Exception ex)
                {
                    log("[警告] 自动启动 Antigravity 失败: " + ex.Message);
                }
            }

            progress(100, "汉化完成");
            return true;
        }

        public async Task<bool> ExecuteRestoreAsync(
            string installDir,
            bool noKill,
            Action<string> log,
            Action<int, string> progress)
        {
            string nodeExe = await EnsureNodeAsync(log, progress);
            string repoDir = await EnsureRepoAsync(log, progress);

            progress(78, "正在还原官方英文原版...");
            List<string> args = new List<string>();
            args.Add(QuoteArg(Path.Combine(repoDir, "localization_engine.js")));
            args.Add("--huifu");
            if (noKill) args.Add("--no-kill");
            args.Add("--install-dir");
            args.Add(QuoteArg(installDir));

            int exitCode = await RunEngineProcessAsync(nodeExe, repoDir, string.Join(" ", args.ToArray()), log);
            progress(100, exitCode == 0 ? "已还原为官方英文" : "还原失败");
            return exitCode == 0;
        }

        private static Task<int> RunEngineProcessAsync(
            string nodeExe,
            string repoDir,
            string arguments,
            Action<string> log)
        {
            return Task.Run(delegate()
            {
                log("执行引擎: \"" + nodeExe + "\" " + arguments);
                ProcessStartInfo psi = new ProcessStartInfo(nodeExe, arguments);
                psi.WorkingDirectory = repoDir;
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;

                using (Process proc = new Process())
                {
                    proc.StartInfo = psi;
                    proc.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) log(e.Data);
                    };
                    proc.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) log("[ERR] " + e.Data);
                    };

                    proc.Start();
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();
                    proc.WaitForExit();
                    // Parameterless WaitForExit guarantees async stdout/stderr events are flushed
                    return proc.ExitCode;
                }
            });
        }

        private static string QuoteArg(string value)
        {
            if (value == null) return "\"\"";
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
            }
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly CliOptions _options;
        private readonly LocalizationService _service;

        private TextBox _txtInstallDir;
        private Button _btnBrowse;
        private Label _lblVersionValue;
        private Label _lblStatusBadge;
        private ComboBox _cmbLang;
        private ComboBox _cmbBrand;
        private CheckBox _chkAutoLaunch;
        private Button _btnInstall;
        private Button _btnRestore;
        private Button _btnCancelAuto;
        private Button _btnRefresh;
        private ProgressBar _progressBar;
        private Label _lblProgressStatus;
        private TextBox _txtLog;

        private System.Windows.Forms.Timer _countdownTimer;
        private int _countdownSeconds;
        private bool _isBusy;

        public MainForm(CliOptions options)
        {
            _options = options ?? new CliOptions();
            _service = new LocalizationService();
            InitializeUi();
            LoadInitialState();
        }

        private void InitializeUi()
        {
            this.Text = "Antigravity 一键汉化工具";
            this.ClientSize = new Size(680, 540);
            this.MinimumSize = new Size(640, 500);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(248, 250, 252);
            this.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Regular);

            try
            {
                Icon appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (appIcon != null) this.Icon = appIcon;
            }
            catch
            {
            }

            // Header banner
            Panel headerPanel = new Panel();
            headerPanel.Dock = DockStyle.Top;
            headerPanel.Height = 76;
            headerPanel.BackColor = Color.FromArgb(30, 41, 59);
            headerPanel.Padding = new Padding(18, 12, 18, 10);

            Label lblTitle = new Label();
            lblTitle.Text = "Antigravity 一键汉化工具";
            lblTitle.ForeColor = Color.White;
            lblTitle.Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold);
            lblTitle.AutoSize = true;
            lblTitle.Location = new Point(18, 12);

            Label lblSubtitle = new Label();
            lblSubtitle.Text = "自动同步 GitHub 开源汉化包 · 零依赖一键部署";
            lblSubtitle.ForeColor = Color.FromArgb(148, 163, 184);
            lblSubtitle.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular);
            lblSubtitle.AutoSize = true;
            lblSubtitle.Location = new Point(20, 44);

            LinkLabel lnkRepo = new LinkLabel();
            lnkRepo.Text = "GitHub: qqxpee/antigravity2-cn";
            lnkRepo.LinkColor = Color.FromArgb(147, 197, 253);
            lnkRepo.ActiveLinkColor = Color.White;
            lnkRepo.VisitedLinkColor = Color.FromArgb(147, 197, 253);
            lnkRepo.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular);
            lnkRepo.AutoSize = true;
            lnkRepo.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            lnkRepo.Location = new Point(460, 44);
            lnkRepo.LinkClicked += delegate(object s, LinkLabelLinkClickedEventArgs e)
            {
                try { Process.Start(LocalizationService.RepoDisplayUrl); } catch { }
            };

            headerPanel.Controls.Add(lblTitle);
            headerPanel.Controls.Add(lblSubtitle);
            headerPanel.Controls.Add(lnkRepo);

            // Status panel
            Panel statusCard = new Panel();
            statusCard.Location = new Point(18, 90);
            statusCard.Size = new Size(644, 92);
            statusCard.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            statusCard.BackColor = Color.White;
            statusCard.BorderStyle = BorderStyle.FixedSingle;

            Label lblDir = new Label();
            lblDir.Text = "安装目录:";
            lblDir.AutoSize = true;
            lblDir.Location = new Point(14, 17);

            _txtInstallDir = new TextBox();
            _txtInstallDir.Location = new Point(88, 14);
            _txtInstallDir.Size = new Size(452, 25);
            _txtInstallDir.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _txtInstallDir.TextChanged += delegate { CancelAutoCountdown(); };

            _btnBrowse = new Button();
            _btnBrowse.Text = "浏览...";
            _btnBrowse.Location = new Point(548, 13);
            _btnBrowse.Size = new Size(80, 27);
            _btnBrowse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnBrowse.FlatStyle = FlatStyle.System;
            _btnBrowse.Click += OnBrowseClick;

            Label lblVer = new Label();
            lblVer.Text = "软件版本:";
            lblVer.AutoSize = true;
            lblVer.Location = new Point(14, 55);

            _lblVersionValue = new Label();
            _lblVersionValue.Text = "-";
            _lblVersionValue.AutoSize = true;
            _lblVersionValue.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
            _lblVersionValue.Location = new Point(88, 55);

            Label lblStat = new Label();
            lblStat.Text = "当前状态:";
            lblStat.AutoSize = true;
            lblStat.Location = new Point(250, 55);

            _lblStatusBadge = new Label();
            _lblStatusBadge.Text = "检测中...";
            _lblStatusBadge.AutoSize = false;
            _lblStatusBadge.TextAlign = ContentAlignment.MiddleCenter;
            _lblStatusBadge.Size = new Size(124, 24);
            _lblStatusBadge.Location = new Point(324, 51);
            _lblStatusBadge.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
            _lblStatusBadge.ForeColor = Color.White;
            _lblStatusBadge.BackColor = Color.Gray;

            statusCard.Controls.Add(lblDir);
            statusCard.Controls.Add(_txtInstallDir);
            statusCard.Controls.Add(_btnBrowse);
            statusCard.Controls.Add(lblVer);
            statusCard.Controls.Add(_lblVersionValue);
            statusCard.Controls.Add(lblStat);
            statusCard.Controls.Add(_lblStatusBadge);

            // Options row
            Panel optionsCard = new Panel();
            optionsCard.Location = new Point(18, 192);
            optionsCard.Size = new Size(644, 48);
            optionsCard.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            optionsCard.BackColor = Color.White;
            optionsCard.BorderStyle = BorderStyle.FixedSingle;

            Label lblLang = new Label();
            lblLang.Text = "语言:";
            lblLang.AutoSize = true;
            lblLang.Location = new Point(14, 14);

            _cmbLang = new ComboBox();
            _cmbLang.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbLang.Location = new Point(56, 11);
            _cmbLang.Size = new Size(124, 26);
            _cmbLang.Items.Add("简体中文 (推荐)");
            _cmbLang.Items.Add("繁體中文");
            _cmbLang.SelectedIndex = _options.UseTw ? 1 : 0;
            _cmbLang.SelectedIndexChanged += delegate { CancelAutoCountdown(); };

            Label lblBrand = new Label();
            lblBrand.Text = "左上角品牌:";
            lblBrand.AutoSize = true;
            lblBrand.Location = new Point(194, 14);

            _cmbBrand = new ComboBox();
            _cmbBrand.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbBrand.Location = new Point(276, 11);
            _cmbBrand.Size = new Size(168, 26);
            _cmbBrand.Items.Add("保留 Antigravity (推荐)");
            _cmbBrand.Items.Add("隐藏品牌名");
            _cmbBrand.Items.Add("中文品牌名");
            if (_options.BrandTitle == "hidden") _cmbBrand.SelectedIndex = 1;
            else if (_options.BrandTitle == "translated") _cmbBrand.SelectedIndex = 2;
            else _cmbBrand.SelectedIndex = 0;
            _cmbBrand.SelectedIndexChanged += delegate { CancelAutoCountdown(); };

            _chkAutoLaunch = new CheckBox();
            _chkAutoLaunch.Text = "完成后自动启动";
            _chkAutoLaunch.AutoSize = true;
            _chkAutoLaunch.Location = new Point(462, 13);
            _chkAutoLaunch.Checked = !_options.NoLaunch;
            _chkAutoLaunch.CheckedChanged += delegate { CancelAutoCountdown(); };

            optionsCard.Controls.Add(lblLang);
            optionsCard.Controls.Add(_cmbLang);
            optionsCard.Controls.Add(lblBrand);
            optionsCard.Controls.Add(_cmbBrand);
            optionsCard.Controls.Add(_chkAutoLaunch);

            // Action buttons row
            _btnInstall = new Button();
            _btnInstall.Text = "一键汉化 / 更新";
            _btnInstall.Location = new Point(18, 252);
            _btnInstall.Size = new Size(164, 36);
            _btnInstall.FlatStyle = FlatStyle.Flat;
            _btnInstall.FlatAppearance.BorderSize = 0;
            _btnInstall.BackColor = Color.FromArgb(37, 99, 235);
            _btnInstall.ForeColor = Color.White;
            _btnInstall.Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);
            _btnInstall.Cursor = Cursors.Hand;
            _btnInstall.Click += delegate { StartInstallFlow(); };

            _btnRestore = new Button();
            _btnRestore.Text = "还原官方英文";
            _btnRestore.Location = new Point(194, 252);
            _btnRestore.Size = new Size(134, 36);
            _btnRestore.FlatStyle = FlatStyle.System;
            _btnRestore.Click += delegate { StartRestoreFlow(); };

            _btnCancelAuto = new Button();
            _btnCancelAuto.Text = "取消自动";
            _btnCancelAuto.Location = new Point(340, 252);
            _btnCancelAuto.Size = new Size(100, 36);
            _btnCancelAuto.FlatStyle = FlatStyle.Flat;
            _btnCancelAuto.BackColor = Color.FromArgb(254, 243, 199);
            _btnCancelAuto.ForeColor = Color.FromArgb(146, 64, 14);
            _btnCancelAuto.FlatAppearance.BorderColor = Color.FromArgb(245, 158, 11);
            _btnCancelAuto.Visible = false;
            _btnCancelAuto.Click += delegate { CancelAutoCountdown(); };

            _btnRefresh = new Button();
            _btnRefresh.Text = "刷新状态";
            _btnRefresh.Location = new Point(562, 252);
            _btnRefresh.Size = new Size(100, 36);
            _btnRefresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnRefresh.FlatStyle = FlatStyle.System;
            _btnRefresh.Click += delegate
            {
                CancelAutoCountdown();
                RefreshStatus();
            };

            // Progress area
            _lblProgressStatus = new Label();
            _lblProgressStatus.Text = "准备就绪";
            _lblProgressStatus.Location = new Point(18, 298);
            _lblProgressStatus.Size = new Size(644, 20);
            _lblProgressStatus.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _lblProgressStatus.ForeColor = Color.FromArgb(51, 65, 85);

            _progressBar = new ProgressBar();
            _progressBar.Location = new Point(18, 322);
            _progressBar.Size = new Size(644, 12);
            _progressBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _progressBar.Minimum = 0;
            _progressBar.Maximum = 100;
            _progressBar.Value = 0;

            // Log box
            _txtLog = new TextBox();
            _txtLog.Location = new Point(18, 344);
            _txtLog.Size = new Size(644, 180);
            _txtLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _txtLog.Multiline = true;
            _txtLog.ReadOnly = true;
            _txtLog.ScrollBars = ScrollBars.Vertical;
            _txtLog.BackColor = Color.FromArgb(24, 24, 27);
            _txtLog.ForeColor = Color.FromArgb(228, 228, 231);
            _txtLog.Font = new Font("Consolas", 9.5f, FontStyle.Regular);
            _txtLog.BorderStyle = BorderStyle.FixedSingle;

            this.Controls.Add(headerPanel);
            this.Controls.Add(statusCard);
            this.Controls.Add(optionsCard);
            this.Controls.Add(_btnInstall);
            this.Controls.Add(_btnRestore);
            this.Controls.Add(_btnCancelAuto);
            this.Controls.Add(_btnRefresh);
            this.Controls.Add(_lblProgressStatus);
            this.Controls.Add(_progressBar);
            this.Controls.Add(_txtLog);

            _countdownTimer = new System.Windows.Forms.Timer();
            _countdownTimer.Interval = 1000;
            _countdownTimer.Tick += OnCountdownTick;

            this.Shown += OnFormShown;
        }

        private void LoadInitialState()
        {
            string dir = !string.IsNullOrEmpty(_options.InstallDir)
                ? _options.InstallDir
                : _service.DetectInstallDir();

            _txtInstallDir.Text = dir ?? string.Empty;
            RefreshStatus();
        }

        private void OnFormShown(object sender, EventArgs e)
        {
            if (!_options.NoAuto && _service.IsValidInstallDir(_txtInstallDir.Text.Trim()))
            {
                _countdownSeconds = 2;
                _btnCancelAuto.Visible = true;
                UpdateCountdownLabel();
                _countdownTimer.Start();
            }
        }

        private void OnCountdownTick(object sender, EventArgs e)
        {
            _countdownSeconds--;
            if (_countdownSeconds <= 0)
            {
                _countdownTimer.Stop();
                _btnCancelAuto.Visible = false;
                StartInstallFlow();
            }
            else
            {
                UpdateCountdownLabel();
            }
        }

        private void UpdateCountdownLabel()
        {
            _lblProgressStatus.Text = string.Format("{0} 秒后自动开始一键汉化...（点击任意选项或“取消自动”可中断）", _countdownSeconds);
        }

        private void CancelAutoCountdown()
        {
            if (_countdownTimer != null && _countdownTimer.Enabled)
            {
                _countdownTimer.Stop();
                _btnCancelAuto.Visible = false;
                if (!_isBusy)
                {
                    _lblProgressStatus.Text = "已取消自动运行，请选择操作。";
                }
            }
        }

        private void OnBrowseClick(object sender, EventArgs e)
        {
            CancelAutoCountdown();
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "请选择包含 Antigravity.exe 的安装目录";
                if (!string.IsNullOrEmpty(_txtInstallDir.Text) && Directory.Exists(_txtInstallDir.Text))
                {
                    dlg.SelectedPath = _txtInstallDir.Text;
                }
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _txtInstallDir.Text = dlg.SelectedPath;
                    RefreshStatus();
                }
            }
        }

        private void RefreshStatus()
        {
            string dir = _txtInstallDir.Text.Trim();
            if (!_service.IsValidInstallDir(dir))
            {
                _lblVersionValue.Text = "-";
                _lblStatusBadge.Text = "未检测到安装";
                _lblStatusBadge.BackColor = Color.FromArgb(220, 38, 38);
                return;
            }

            _lblVersionValue.Text = _service.GetAppVersion(dir);
            if (_service.IsLocalized(dir))
            {
                _lblStatusBadge.Text = "已汉化";
                _lblStatusBadge.BackColor = Color.FromArgb(22, 163, 74);
            }
            else
            {
                _lblStatusBadge.Text = "未汉化";
                _lblStatusBadge.BackColor = Color.FromArgb(234, 88, 12);
            }
        }

        private string SelectedBrandMode()
        {
            if (_cmbBrand.SelectedIndex == 1) return "hidden";
            if (_cmbBrand.SelectedIndex == 2) return "translated";
            return "english";
        }

        private async void StartInstallFlow()
        {
            CancelAutoCountdown();
            if (_isBusy) return;

            string dir = _txtInstallDir.Text.Trim();
            if (!_service.IsValidInstallDir(dir))
            {
                MessageBox.Show(this, "未找到有效的 Antigravity 安装目录（需包含 Antigravity.exe 和 resources\\app.asar）。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            bool useTw = (_cmbLang.SelectedIndex == 1);
            string brand = SelectedBrandMode();
            bool autoLaunch = _chkAutoLaunch.Checked;

            SetBusyState(true);
            _txtLog.Clear();
            AppendLog("开始执行一键汉化...");

            bool ok = false;
            try
            {
                ok = await _service.ExecuteInstallAsync(
                    dir,
                    useTw,
                    brand,
                    _options.NoKill,
                    autoLaunch,
                    AppendLog,
                    UpdateProgress);
            }
            catch (Exception ex)
            {
                AppendLog("[错误] " + ex.Message);
            }
            finally
            {
                SetBusyState(false);
                RefreshStatus();
            }

            if (ok)
            {
                AppendLog("[√] 一键汉化已全部完成！");
                _lblProgressStatus.Text = "汉化成功！Antigravity 界面已更新为中文。";
            }
            else
            {
                _lblProgressStatus.Text = "汉化失败，请查看下方日志信息。";
                MessageBox.Show(this, "汉化过程中出现错误，请查看日志窗口中的详细信息。", "汉化失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void StartRestoreFlow()
        {
            CancelAutoCountdown();
            if (_isBusy) return;

            string dir = _txtInstallDir.Text.Trim();
            if (!_service.IsValidInstallDir(dir))
            {
                MessageBox.Show(this, "未找到有效的 Antigravity 安装目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SetBusyState(true);
            _txtLog.Clear();
            AppendLog("开始还原官方英文原版...");

            bool ok = false;
            try
            {
                ok = await _service.ExecuteRestoreAsync(
                    dir,
                    _options.NoKill,
                    AppendLog,
                    UpdateProgress);
            }
            catch (Exception ex)
            {
                AppendLog("[错误] " + ex.Message);
            }
            finally
            {
                SetBusyState(false);
                RefreshStatus();
            }

            if (ok)
            {
                AppendLog("[√] 已还原为官方英文原版！");
                _lblProgressStatus.Text = "已成功还原官方英文。";
            }
            else
            {
                _lblProgressStatus.Text = "还原失败，请查看下方日志信息。";
                MessageBox.Show(this, "还原过程中出现错误，请查看日志窗口中的详细信息。", "还原失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SetBusyState(bool busy)
        {
            _isBusy = busy;
            _btnInstall.Enabled = !busy;
            _btnRestore.Enabled = !busy;
            _btnBrowse.Enabled = !busy;
            _btnRefresh.Enabled = !busy;
            _txtInstallDir.Enabled = !busy;
            _cmbLang.Enabled = !busy;
            _cmbBrand.Enabled = !busy;
            _chkAutoLaunch.Enabled = !busy;
            if (busy)
            {
                _progressBar.Value = 5;
            }
        }

        private void UpdateProgress(int pct, string statusText)
        {
            if (this.IsDisposed) return;
            if (this.InvokeRequired)
            {
                try
                {
                    this.BeginInvoke((Action)delegate { UpdateProgress(pct, statusText); });
                }
                catch
                {
                }
                return;
            }

            int clamped = Math.Max(0, Math.Min(100, pct));
            _progressBar.Value = clamped;
            if (!string.IsNullOrEmpty(statusText))
            {
                _lblProgressStatus.Text = statusText;
            }
        }

        private void AppendLog(string message)
        {
            if (this.IsDisposed) return;
            if (this.InvokeRequired)
            {
                try
                {
                    this.BeginInvoke((Action)delegate { AppendLog(message); });
                }
                catch
                {
                }
                return;
            }

            string line = string.Format("[{0}] {1}", DateTime.Now.ToString("HH:mm:ss"), message);
            _txtLog.AppendText(line + Environment.NewLine);
            _txtLog.SelectionStart = _txtLog.TextLength;
            _txtLog.ScrollToCaret();
        }
    }
}
