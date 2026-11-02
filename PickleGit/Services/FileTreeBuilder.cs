using System;
using System.Collections.Generic;
using System.Linq;
using PickleGit.Models;

namespace PickleGit.Services
{
    /// <summary>Groups a flat FileChange list into a folder hierarchy, then flattens it back into a
    /// single row list for a virtualized ListView — the same flatten-a-tree-into-a-ListView pattern
    /// used for the sidebar's branch tree (see CLAUDE.md), since this codebase deliberately avoids
    /// real TreeView controls.</summary>
    public static class FileTreeBuilder
    {
        private class Node
        {
            public readonly Dictionary<string, Node> Children =
                new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
            public readonly List<FileChange> Files = new List<FileChange>();
        }

        /// <summary>Builds the flattened tree. A folder present in <paramref name="collapsedFolders"/>
        /// still gets its own row (so it can be expanded again), but none of its descendant
        /// files/subfolders are emitted — expanded is the default for every folder.</summary>
        public static List<FileTreeRow> Build(IEnumerable<FileChange> files, Comparison<FileChange> leafComparer,
            ISet<string> collapsedFolders)
        {
            var root = new Node();
            foreach (var f in files)
            {
                var parts = (f.Path ?? string.Empty).Split('/');
                var node = root;
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    var part = parts[i];
                    if (!node.Children.TryGetValue(part, out var child))
                    {
                        child = new Node();
                        node.Children[part] = child;
                    }
                    node = child;
                }
                node.Files.Add(f);
            }

            var rows = new List<FileTreeRow>();
            Flatten(root, 0, string.Empty, rows, leafComparer, collapsedFolders);
            return rows;
        }

        private static void Flatten(Node node, int indent, string pathPrefix, List<FileTreeRow> rows,
            Comparison<FileChange> leafComparer, ISet<string> collapsedFolders)
        {
            foreach (var kvp in node.Children.OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase))
            {
                var folderPath = pathPrefix.Length == 0 ? kvp.Key : pathPrefix + "/" + kvp.Key;
                bool expanded = !collapsedFolders.Contains(folderPath);
                var descendants = new List<FileChange>();
                CollectFiles(kvp.Value, descendants);
                rows.Add(new FileTreeRow
                {
                    Kind = FileTreeRowKind.Folder,
                    IndentLevel = indent,
                    DisplayName = kvp.Key,
                    FullPath = folderPath,
                    IsExpanded = expanded,
                    Files = descendants
                });
                if (expanded)
                    Flatten(kvp.Value, indent + 1, folderPath, rows, leafComparer, collapsedFolders);
            }

            var files = node.Files.ToList();
            files.Sort(leafComparer);
            foreach (var f in files)
            {
                rows.Add(new FileTreeRow
                {
                    Kind = FileTreeRowKind.File,
                    IndentLevel = indent,
                    DisplayName = System.IO.Path.GetFileName(f.Path),
                    File = f
                });
            }
        }

        private static void CollectFiles(Node node, List<FileChange> into)
        {
            foreach (var child in node.Children.OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase))
                CollectFiles(child.Value, into);
            into.AddRange(node.Files);
        }

        // ── Aggregated (multi-select) variant ───────────────────────────────────────────────
        // Same flatten-a-tree algorithm as above, duplicated rather than made generic: the two
        // models (FileChange vs AggregatedFileChange) are XAML-bound through separate
        // DataTemplateSelectors (a generic row type doesn't map cleanly onto XAML's DataType
        // template matching), and the two panels' actions differ (stage/unstage vs read-only).

        private class AggregatedNode
        {
            public readonly Dictionary<string, AggregatedNode> Children =
                new Dictionary<string, AggregatedNode>(StringComparer.OrdinalIgnoreCase);
            public readonly List<AggregatedFileChange> Files = new List<AggregatedFileChange>();
        }

        public static List<AggregatedFileTreeRow> BuildAggregated(IEnumerable<AggregatedFileChange> files,
            Comparison<AggregatedFileChange> leafComparer, ISet<string> collapsedFolders)
        {
            var root = new AggregatedNode();
            foreach (var f in files)
            {
                var parts = (f.Path ?? string.Empty).Split('/');
                var node = root;
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    var part = parts[i];
                    if (!node.Children.TryGetValue(part, out var child))
                    {
                        child = new AggregatedNode();
                        node.Children[part] = child;
                    }
                    node = child;
                }
                node.Files.Add(f);
            }

            var rows = new List<AggregatedFileTreeRow>();
            FlattenAggregated(root, 0, string.Empty, rows, leafComparer, collapsedFolders);
            return rows;
        }

        private static void FlattenAggregated(AggregatedNode node, int indent, string pathPrefix,
            List<AggregatedFileTreeRow> rows, Comparison<AggregatedFileChange> leafComparer,
            ISet<string> collapsedFolders)
        {
            foreach (var kvp in node.Children.OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase))
            {
                var folderPath = pathPrefix.Length == 0 ? kvp.Key : pathPrefix + "/" + kvp.Key;
                bool expanded = !collapsedFolders.Contains(folderPath);
                rows.Add(new AggregatedFileTreeRow
                {
                    Kind = FileTreeRowKind.Folder,
                    IndentLevel = indent,
                    DisplayName = kvp.Key,
                    FullPath = folderPath,
                    IsExpanded = expanded
                });
                if (expanded)
                    FlattenAggregated(kvp.Value, indent + 1, folderPath, rows, leafComparer, collapsedFolders);
            }

            var files = node.Files.ToList();
            files.Sort(leafComparer);
            foreach (var f in files)
            {
                rows.Add(new AggregatedFileTreeRow
                {
                    Kind = FileTreeRowKind.File,
                    IndentLevel = indent,
                    DisplayName = System.IO.Path.GetFileName(f.Path),
                    File = f
                });
            }
        }
    }
}
