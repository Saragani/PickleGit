namespace PickleGit.Models
{
    public class BranchInfo
    {
        public string Name { get; set; }
        public string FullName { get; set; }
        public bool IsRemote { get; set; }
        public bool IsHead { get; set; }
        public string RemoteName { get; set; }
        public string TrackedBranchName { get; set; }
        public string TipSha { get; set; }
        public int AheadBy { get; set; }
        public int BehindBy { get; set; }

        public string DisplayName => IsRemote && RemoteName != null
            ? Name.StartsWith(RemoteName + "/") ? Name.Substring(RemoteName.Length + 1) : Name
            : Name;
    }

    public class TagInfo
    {
        public string Name { get; set; }
        public string TargetSha { get; set; }
        public bool IsAnnotated { get; set; }
        public string Message { get; set; }
    }

    public class RemoteInfo
    {
        public string Name { get; set; }
        public string Url { get; set; }
        public string PushUrl { get; set; }
    }

    public class StashInfo
    {
        public int Index { get; set; }
        public string Message { get; set; }
        public string Sha { get; set; }
    }

    public class SubmoduleInfo
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string Url { get; set; }
        public bool IsInitialized { get; set; }
        public bool IsDirty { get; set; }
        public string StatusLabel => !IsInitialized ? "not initialized" : IsDirty ? "modified" : "clean";
    }

    public class WorktreeInfo
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string Branch { get; set; }
        public bool IsLocked { get; set; }
        public bool IsMain { get; set; }
    }
}
