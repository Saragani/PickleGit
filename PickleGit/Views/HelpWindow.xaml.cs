using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Markdig;
using Markdig.Wpf;

namespace PickleGit.Views
{
    public partial class HelpWindow : Window
    {
        private class Topic
        {
            public string Title { get; set; }
            public string FileName { get; set; }
        }

        // Adding a topic later: drop a new .md (and any screenshots) into Resources/Help/,
        // add one line here — no other code changes needed.
        private static readonly Topic[] Topics =
        {
            new Topic { Title = "Getting Started",         FileName = "01-getting-started.md" },
            new Topic { Title = "The Commit Graph",         FileName = "02-commit-graph.md" },
            new Topic { Title = "Staging & Committing",     FileName = "03-staging-and-committing.md" },
            new Topic { Title = "The Diff View",            FileName = "04-diff-view.md" },
            new Topic { Title = "Branches & Stashes",       FileName = "05-branches-and-stashes.md" },
            new Topic { Title = "Syncing",                  FileName = "06-syncing.md" },
            new Topic { Title = "Pull Requests",            FileName = "07-pull-requests.md" },
            new Topic { Title = "History Tools",            FileName = "08-history-tools.md" },
            new Topic { Title = "Settings & Preferences",   FileName = "09-settings.md" },
            new Topic { Title = "Keyboard Shortcuts",       FileName = "10-keyboard-shortcuts.md" },
            new Topic { Title = "Context Menus",             FileName = "11-context-menus.md" },
        };

        private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseSupportedExtensions().Build();

        public HelpWindow()
        {
            InitializeComponent();
            ContentViewer.Pipeline = Pipeline;
            TopicList.ItemsSource = Topics;
            TopicList.SelectedIndex = 0;
        }

        private void TopicList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(TopicList.SelectedItem is Topic topic)) return;

            var uri = new Uri("pack://application:,,,/Resources/Help/" + topic.FileName);
            using (var stream = Application.GetResourceStream(uri)?.Stream)
            using (var reader = stream != null ? new StreamReader(stream) : null)
            {
                if (reader == null) return;
                var text = reader.ReadToEnd();
                // Rewrite doc-relative image paths to pack URIs so Markdig.Wpf's image
                // renderer (which resolves Uri strings as-is) can load them.
                text = Regex.Replace(text, @"\(Screenshots/", "(pack://application:,,,/Resources/Help/Screenshots/");
                ContentViewer.Markdown = text;
            }
        }

        private void Hyperlink_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var url = e.Parameter?.ToString();
            if (string.IsNullOrEmpty(url)) return;

            if (url.EndsWith(".md"))
            {
                var target = Array.Find(Topics, t => t.FileName == url);
                if (target != null) TopicList.SelectedItem = target;
                return;
            }

            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { /* ignore malformed/unsupported links */ }
        }
    }
}
