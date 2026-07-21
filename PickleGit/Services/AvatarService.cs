using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PickleGit.Services
{
    /// <summary>
    /// Resolves a small avatar image for a commit author email — GitHub noreply addresses map
    /// directly to a GitHub avatar, everything else falls back to Gravatar (d=404, so a genuine
    /// "no avatar" is distinguishable from "not fetched yet"). Disk-cached at
    /// %APPDATA%\PickleGit\avatars\&lt;md5(email)&gt;.png; in-memory cache holds decoded, frozen
    /// BitmapImages so callers can use them directly from the UI thread from any caller thread.
    /// </summary>
    public static class AvatarService
    {
        private const int Size = 64;
        private static readonly string CacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PickleGit", "avatars");
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        private static readonly Dictionary<string, ImageSource> Cache =
            new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> NoAvatar =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> InFlight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Fired on the UI thread once an avatar finishes loading (email is the raw, untrimmed key).</summary>
        public static event Action<string> AvatarReady;

        /// <summary>Non-blocking: returns a cached image if available, else null and kicks off a background fetch.</summary>
        public static ImageSource TryGet(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return null;
            var key = email.Trim().ToLowerInvariant();
            lock (Cache)
            {
                if (Cache.TryGetValue(key, out var img)) return img;
                if (NoAvatar.Contains(key)) return null;
            }

            lock (InFlight)
            {
                if (!InFlight.Add(key)) return null;
            }
            _ = FetchAsync(key);
            return null;
        }

        private static async Task FetchAsync(string email)
        {
            try
            {
                var img = await LoadAsync(email).ConfigureAwait(false);
                if (img != null)
                {
                    lock (Cache) { Cache[email] = img; }
                    Application.Current?.Dispatcher.BeginInvoke(new Action(() => AvatarReady?.Invoke(email)));
                }
                else
                {
                    lock (Cache) { NoAvatar.Add(email); }
                }
            }
            catch { lock (Cache) { NoAvatar.Add(email); } }
            finally { lock (InFlight) { InFlight.Remove(email); } }
        }

        private static async Task<ImageSource> LoadAsync(string email)
        {
            var hash = Md5Hex(email);
            var diskPath = Path.Combine(CacheDir, hash + ".png");

            byte[] bytes = null;
            try
            {
                if (File.Exists(diskPath))
                    bytes = await Task.Run(() => File.ReadAllBytes(diskPath)).ConfigureAwait(false);
            }
            catch { /* corrupt cache entry — refetch */ }

            if (bytes == null || bytes.Length == 0)
            {
                bytes = await DownloadAsync(email).ConfigureAwait(false);
                if (bytes == null || bytes.Length == 0) return null;
                try
                {
                    Directory.CreateDirectory(CacheDir);
                    await Task.Run(() => File.WriteAllBytes(diskPath, bytes)).ConfigureAwait(false);
                }
                catch { /* best-effort cache write */ }
            }

            return Decode(bytes);
        }

        private static async Task<byte[]> DownloadAsync(string email)
        {
            var githubUser = ExtractGitHubNoReplyUsername(email);
            if (githubUser != null)
            {
                try
                {
                    // githubUser comes from a commit author's email — untrusted for any repo the
                    // user opens — so it must be escaped before entering the URL, not interpolated raw.
                    var bytes = await Http.GetByteArrayAsync($"https://github.com/{Uri.EscapeDataString(githubUser)}.png?size={Size}").ConfigureAwait(false);
                    if (bytes.Length > 0) return bytes;
                }
                catch { /* fall through to gravatar */ }
            }

            try
            {
                var hash = Md5Hex(email);
                var response = await Http.GetAsync($"https://www.gravatar.com/avatar/{hash}?s={Size}&d=404").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null; // 404 == genuinely no gravatar
                return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }
            catch { return null; }
        }

        /// <summary>"12345+username@users.noreply.github.com" or "username@users.noreply.github.com" → username.</summary>
        private static string ExtractGitHubNoReplyUsername(string email)
        {
            const string suffix = "@users.noreply.github.com";
            if (!email.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return null;
            var local = email.Substring(0, email.Length - suffix.Length);
            var plus = local.IndexOf('+');
            return plus >= 0 ? local.Substring(plus + 1) : local;
        }

        private static ImageSource Decode(byte[] bytes)
        {
            try
            {
                var bmp = new BitmapImage();
                using (var ms = new MemoryStream(bytes))
                {
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.DecodePixelWidth = Size;
                    bmp.EndInit();
                }
                bmp.Freeze(); // safe to hand to the UI thread from a background thread
                return bmp;
            }
            catch { return null; }
        }

        private static string Md5Hex(string input)
        {
            using (var md5 = MD5.Create())
            {
                var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
