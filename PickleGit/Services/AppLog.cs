using System;
using System.IO;
using System.Text;

namespace PickleGit.Services
{
    /// <summary>
    /// Minimal rolling file logger — %APPDATA%\PickleGit\logs\picklegit.log, rotated once
    /// past 1 MB (single .old backup). Logging must never throw: every entry point is
    /// fully guarded, so callers can log from catch blocks without re-entering failure.
    /// </summary>
    public static class AppLog
    {
        private const long MaxBytes = 1024 * 1024;
        private static readonly object Sync = new object();
        private static readonly string LogDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PickleGit", "logs");
        private static readonly string LogPath = Path.Combine(LogDir, "picklegit.log");

        public static void Info(string message) => Write("INFO ", message, null);
        public static void Warn(string message, Exception ex = null) => Write("WARN ", message, ex);
        public static void Error(string message, Exception ex = null) => Write("ERROR", message, ex);

        private static void Write(string level, string message, Exception ex)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                  .Append(' ').Append(level).Append(' ').Append(message);
                if (ex != null)
                    sb.Append(" — ").Append(ex.GetType().Name).Append(": ").Append(ex.Message)
                      .AppendLine().Append(ex.StackTrace);
                sb.AppendLine();

                lock (Sync)
                {
                    Directory.CreateDirectory(LogDir);
                    var info = new FileInfo(LogPath);
                    if (info.Exists && info.Length > MaxBytes)
                    {
                        var old = LogPath + ".old";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(LogPath, old);
                    }
                    File.AppendAllText(LogPath, sb.ToString());
                }
            }
            catch { /* logging must never throw */ }
        }
    }
}
