using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PickleGit.Models;

namespace PickleGit.Services.Git
{
    /// <summary>
    /// Builds the on-disk todo script for `git rebase -i` from the dialog's RebaseTodoItem list
    /// (extracted from RepositoryViewModel — pure file/temp-dir work, no VM state).
    ///
    /// Reword becomes `pick` + `exec git commit --amend -F &lt;msgfile&gt;` so no interactive editor
    /// is ever needed; Break becomes `pick` + a standalone `break` line.
    ///
    /// Git for Windows runs GIT_SEQUENCE_EDITOR and `exec` lines through its bundled MSYS `sh`,
    /// so every path handed to git must be wrapped with <see cref="ShellSingleQuote"/> — a bare
    /// Windows path gets its backslashes eaten by sh's word-splitting.
    /// </summary>
    public static class RebaseTodoBuilder
    {
        /// <summary>Writes todo.txt (+ reword message files) into a fresh temp dir and returns
        /// the GIT_SEQUENCE_EDITOR value that overwrites git's generated todo with ours.</summary>
        public static string BuildSequenceEditor(IReadOnlyList<RebaseTodoItem> items)
        {
            var baseDir = Path.Combine(Path.GetTempPath(), "PickleGit", "rebase");
            CleanupOldTempDirs(baseDir);
            var tempDir = Path.Combine(baseDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            var sb = new StringBuilder();
            int msgFileIndex = 0;
            foreach (var item in items)
            {
                switch (item.Action)
                {
                    case RebaseTodoAction.Drop:
                        sb.AppendLine($"drop {item.Sha} {item.MessageShort}");
                        break;
                    case RebaseTodoAction.Squash:
                        sb.AppendLine($"squash {item.Sha} {item.MessageShort}");
                        break;
                    case RebaseTodoAction.Fixup:
                        sb.AppendLine($"fixup {item.Sha} {item.MessageShort}");
                        break;
                    case RebaseTodoAction.Reword:
                        sb.AppendLine($"pick {item.Sha} {item.MessageShort}");
                        var msgFile = Path.Combine(tempDir, $"msg{msgFileIndex++}.txt");
                        File.WriteAllText(msgFile, item.NewMessage ?? item.Message ?? string.Empty);
                        sb.AppendLine($"exec git commit --amend -F {ShellSingleQuote(msgFile)}");
                        break;
                    case RebaseTodoAction.Edit:
                        sb.AppendLine($"edit {item.Sha} {item.MessageShort}");
                        break;
                    case RebaseTodoAction.Break:
                        sb.AppendLine($"pick {item.Sha} {item.MessageShort}");
                        sb.AppendLine("break");
                        break;
                    default:
                        sb.AppendLine($"pick {item.Sha} {item.MessageShort}");
                        break;
                }
            }

            var todoPath = Path.Combine(tempDir, "todo.txt");
            File.WriteAllText(todoPath, sb.ToString());
            return "cp " + ShellSingleQuote(todoPath) + " ";
        }

        /// <summary>Quotes a path for the POSIX `sh` that Git for Windows uses — single quotes
        /// preserve backslashes literally, unlike double quotes or no quoting at all.</summary>
        public static string ShellSingleQuote(string path) => "'" + path.Replace("'", "'\\''") + "'";

        /// <summary>The temp dir is intentionally NOT deleted right after the rebase starts —
        /// if it stops for conflicts/edit, git still needs the exec message files. Day-old dirs
        /// are reaped on the next rebase instead.</summary>
        private static void CleanupOldTempDirs(string baseDir)
        {
            try
            {
                if (!Directory.Exists(baseDir)) return;
                foreach (var dir in Directory.GetDirectories(baseDir))
                {
                    try
                    {
                        if (Directory.GetLastWriteTimeUtc(dir) < DateTime.UtcNow.AddDays(-1))
                            Directory.Delete(dir, recursive: true);
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
