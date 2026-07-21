using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace PickleGit.Models
{
    public class GraphNode
    {
        public CommitInfo Commit { get; set; }
        public int Lane { get; set; }
        public int TotalLanes { get; set; }
        public List<GraphEdge> Edges { get; set; } = new List<GraphEdge>();

        /// <summary>Lanes active (non-null) before AND after this row that are not converging — drawn as straight-through lines.</summary>
        public List<(int lane, Color color)> PassthroughLanes { get; set; } = new List<(int, Color)>();

        /// <summary>Other lanes that were also tracking this commit's SHA and converge to the node from above (top half).</summary>
        public List<(int lane, Color color)> IncomingLanes { get; set; } = new List<(int, Color)>();

        /// <summary>False when this commit first appears in a new lane (no parent row was tracking it — nothing to draw from above).</summary>
        public bool HasIncomingLine { get; set; } = true;

        public Color LaneColor { get; set; }
    }

    public class GraphEdge
    {
        public int FromLane { get; set; }
        public int ToLane { get; set; }
        public Color Color { get; set; }
        public bool IsMerge { get; set; }
    }

    public static class GraphLayout
    {
        private static readonly Color[] LaneColors = new[]
        {
            Color.FromRgb(0x56, 0xB6, 0xC2), // cyan
            Color.FromRgb(0x6F, 0xBE, 0x72), // green
            Color.FromRgb(0xD1, 0x9A, 0x66), // orange
            Color.FromRgb(0x61, 0xAF, 0xEF), // blue
            Color.FromRgb(0xC6, 0x78, 0xDD), // purple
            Color.FromRgb(0xE0, 0x6C, 0x75), // red/pink
            Color.FromRgb(0xE5, 0xC0, 0x7B), // amber
            Color.FromRgb(0x4B, 0xD4, 0xE0), // teal
            Color.FromRgb(0x98, 0xC3, 0x79), // light green
            Color.FromRgb(0xFF, 0x87, 0x5F), // coral
        };

        public static Color GetLaneColor(int lane) =>
            LaneColors[lane % LaneColors.Length];

        /// <summary>Cheap single-lane layout used while a search filter is active — the filtered
        /// set is not topologically contiguous, so lanes/edges would be misleading anyway, and
        /// skipping the full layout keeps filtering O(n) per keystroke.</summary>
        public static List<GraphNode> ComputeFlat(IList<CommitInfo> commits)
        {
            var result = new List<GraphNode>(commits.Count);
            foreach (var c in commits)
                result.Add(new GraphNode
                {
                    Commit = c,
                    Lane = 0,
                    TotalLanes = 1,
                    LaneColor = GetLaneColor(0),
                    HasIncomingLine = false
                });
            return result;
        }

        public static List<GraphNode> Compute(IList<CommitInfo> commits)
        {
            var result = new List<GraphNode>(commits.Count);
            // lanes[i] = SHA expected next in lane i (null = free)
            var lanes = new List<string>();
            var laneColors = new List<Color>();

            foreach (var commit in commits)
            {
                // Snapshot which lanes are active BEFORE this row
                var activeBefore = new List<int>();
                for (int i = 0; i < lanes.Count; i++)
                    if (lanes[i] != null) activeBefore.Add(i);

                // Find all lanes that are waiting for this commit
                var matchingLanes = new List<int>();
                for (int i = 0; i < lanes.Count; i++)
                    if (lanes[i] == commit.Sha)
                        matchingLanes.Add(i);

                int myLane;
                Color myColor;
                var incomingLanes = new List<(int, Color)>();
                bool hasIncoming = matchingLanes.Count > 0;

                if (matchingLanes.Count == 0)
                {
                    myLane = lanes.IndexOf(null);
                    if (myLane < 0) myLane = lanes.Count;
                    myColor = GetLaneColor(myLane);
                    if (myLane == lanes.Count) { lanes.Add(null); laneColors.Add(myColor); }
                    else laneColors[myLane] = myColor;
                }
                else
                {
                    myLane = matchingLanes[0];
                    myColor = laneColors[myLane];
                    // Capture converging lanes before freeing them
                    for (int i = 1; i < matchingLanes.Count; i++)
                        incomingLanes.Add((matchingLanes[i], laneColors[matchingLanes[i]]));
                    // Free duplicate matching lanes
                    for (int i = matchingLanes.Count - 1; i >= 1; i--)
                        lanes[matchingLanes[i]] = null;
                }

                var edges = new List<GraphEdge>();

                if (commit.ParentShas.Count == 0)
                {
                    lanes[myLane] = null;
                }
                else
                {
                    // First parent continues on my lane
                    lanes[myLane] = commit.ParentShas[0];
                    edges.Add(new GraphEdge { FromLane = myLane, ToLane = myLane, Color = myColor });

                    // Additional parents (merge)
                    for (int p = 1; p < commit.ParentShas.Count; p++)
                    {
                        var pSha = commit.ParentShas[p];
                        int existing = lanes.IndexOf(pSha);
                        int targetLane;
                        Color edgeColor;
                        if (existing >= 0)
                        {
                            targetLane = existing;
                            edgeColor = laneColors[existing];
                        }
                        else
                        {
                            targetLane = lanes.IndexOf(null);
                            if (targetLane < 0) targetLane = lanes.Count;
                            edgeColor = GetLaneColor(targetLane);
                            if (targetLane == lanes.Count) { lanes.Add(pSha); laneColors.Add(edgeColor); }
                            else { lanes[targetLane] = pSha; laneColors[targetLane] = edgeColor; }
                        }
                        edges.Add(new GraphEdge { FromLane = myLane, ToLane = targetLane, Color = edgeColor, IsMerge = true });
                    }
                }

                // Trim trailing nulls
                while (lanes.Count > 0 && lanes[lanes.Count - 1] == null)
                {
                    lanes.RemoveAt(lanes.Count - 1);
                    laneColors.RemoveAt(laneColors.Count - 1);
                }

                // Snapshot active lanes AFTER this row
                var activeAfter = new List<int>();
                for (int i = 0; i < lanes.Count; i++)
                    if (lanes[i] != null) activeAfter.Add(i);

                // Passthrough = active both before and after, not my lane, not a converging lane
                var afterSet = new HashSet<int>(activeAfter);
                var convergingSet = new HashSet<int>();
                foreach (var (ln, _) in incomingLanes) convergingSet.Add(ln);
                var passthroughs = new List<(int, Color)>();
                foreach (var ln in activeBefore)
                {
                    if (ln != myLane && !convergingSet.Contains(ln) && afterSet.Contains(ln))
                        passthroughs.Add((ln, laneColors[ln]));
                }

                int total = Math.Max(myLane + 1, lanes.Count);
                foreach (var e in edges) total = Math.Max(total, Math.Max(e.FromLane, e.ToLane) + 1);
                foreach (var (ln, _) in incomingLanes) total = Math.Max(total, ln + 1);

                result.Add(new GraphNode
                {
                    Commit = commit,
                    Lane = myLane,
                    TotalLanes = total,
                    Edges = edges,
                    PassthroughLanes = passthroughs,
                    IncomingLanes = incomingLanes,
                    HasIncomingLine = hasIncoming,
                    LaneColor = myColor
                });
            }

            return result;
        }
    }
}
