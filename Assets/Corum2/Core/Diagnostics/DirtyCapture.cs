using System;
using System.Collections.Generic;

namespace Corum2.Core
{
    /// <summary>
    /// Completeness rules for a dirty-tree source capture (WP02-M03). A capture is complete only when the patch was
    /// taken and every untracked file inside the source scope was copied with the same bytes as its list row.
    /// </summary>
    public static class DirtyCapture
    {
        public static bool InScope(string path, IEnumerable<string> scope)
        {
            foreach (var s in scope)
                if (path == s || path.StartsWith(s + "/", StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>Path of a porcelain v1 status line ("XY path" or "XY old -> new"), unquoted.</summary>
        public static string StatusPath(string line)
        {
            var p = line.Length > 3 ? line.Substring(3) : "";
            var arrow = p.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow >= 0) p = p.Substring(arrow + 4);
            if (p.Length >= 2 && p[0] == '"' && p[p.Length - 1] == '"') p = p.Substring(1, p.Length - 2);
            return p;
        }

        /// <summary>
        /// Untracked files inside the scope that the capture must keep. "?? file" names a file; "?? dir/" (git collapses
        /// untracked folders) covers every listed path under that folder.
        /// </summary>
        public static List<string> RequiredUntracked(IEnumerable<string> statusLines, IEnumerable<string> listPaths, IEnumerable<string> scope)
        {
            var scopeList = new List<string>(scope);
            var paths = new List<string>(listPaths);
            var set = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var line in statusLines)
            {
                if (!line.StartsWith("?? ", StringComparison.Ordinal)) continue;
                var p = StatusPath(line);
                if (p.EndsWith("/", StringComparison.Ordinal))
                {
                    foreach (var lp in paths)
                        if (lp.StartsWith(p, StringComparison.Ordinal) && InScope(lp, scopeList)) set.Add(lp);
                }
                else if (InScope(p, scopeList)) set.Add(p);
            }
            return new List<string>(set);
        }

        /// <summary>Reasons the capture is incomplete; empty when complete.</summary>
        public static List<string> IncompleteReasons(bool patchTaken, IEnumerable<string> required, ICollection<string> copied,
            IDictionary<string, bool> copyMatchesList)
        {
            var reasons = new List<string>();
            if (!patchTaken) reasons.Add("patch: git diff HEAD --binary failed");
            foreach (var r in required)
            {
                if (!copied.Contains(r)) reasons.Add("untracked file in scope not copied: " + r);
                else if (!copyMatchesList.TryGetValue(r, out var ok) || !ok) reasons.Add("untracked copy differs from its list row: " + r);
            }
            return reasons;
        }
    }
}
