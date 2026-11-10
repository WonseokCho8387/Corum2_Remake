using System.Collections.Generic;
using Corum2.Core;
using NUnit.Framework;

namespace Corum2.Tests
{
    /// <summary>WP02-M03: a dirty capture is complete only with the patch and every in-scope untracked file copied
    /// with its list bytes. End-to-end checks of saved captures are in Tools/validate_manifest.py.</summary>
    public class DirtyCaptureTests
    {
        static readonly string[] Scope = { "Assets", "Packages", "ProjectSettings", "Tools" };

        [Test]
        public void InScope_MatchesFolderPrefixOnly()
        {
            Assert.That(DirtyCapture.InScope("Tools/a.py", Scope), Is.True);
            Assert.That(DirtyCapture.InScope("Assets", Scope), Is.True);
            Assert.That(DirtyCapture.InScope("ToolsX/a.py", Scope), Is.False);
            Assert.That(DirtyCapture.InScope("Evidence/x.txt", Scope), Is.False);
        }

        [Test]
        public void StatusPath_HandlesRenameAndQuotes()
        {
            Assert.That(DirtyCapture.StatusPath(" M Tools/a.py"), Is.EqualTo("Tools/a.py"));
            Assert.That(DirtyCapture.StatusPath("R  Tools/old.py -> Tools/new.py"), Is.EqualTo("Tools/new.py"));
            Assert.That(DirtyCapture.StatusPath("?? \"Tools/a b.txt\""), Is.EqualTo("Tools/a b.txt"));
        }

        [Test]
        public void RequiredUntracked_ExpandsCollapsedFolders_InScopeOnly()
        {
            var status = new[] { " M Tools/a.py", "?? Tools/probe.txt", "?? Tools/newdir/", "?? Evidence/notes.txt", "?? big.bin" };
            var list = new[] { "Tools/a.py", "Tools/newdir/x.txt", "Tools/newdir/sub/y.bin", "Tools/probe.txt", "Tools/other.py" };
            Assert.That(DirtyCapture.RequiredUntracked(status, list, Scope),
                Is.EqualTo(new[] { "Tools/newdir/sub/y.bin", "Tools/newdir/x.txt", "Tools/probe.txt" }));
        }

        [Test]
        public void IncompleteReasons_PatchCopyAndBytes()
        {
            var required = new[] { "Tools/p.txt", "Tools/q.txt", "Tools/r.txt" };
            var copied = new List<string> { "Tools/p.txt", "Tools/q.txt" };
            var matches = new Dictionary<string, bool> { { "Tools/p.txt", true }, { "Tools/q.txt", false } };
            var reasons = DirtyCapture.IncompleteReasons(false, required, copied, matches);
            Assert.That(reasons, Is.EqualTo(new[]
            {
                "patch: git diff HEAD --binary failed",
                "untracked copy differs from its list row: Tools/q.txt",
                "untracked file in scope not copied: Tools/r.txt",
            }));
        }

        [Test]
        public void IncompleteReasons_EmptyWhenComplete()
        {
            var reasons = DirtyCapture.IncompleteReasons(true, new[] { "Tools/p.txt" }, new List<string> { "Tools/p.txt" },
                new Dictionary<string, bool> { { "Tools/p.txt", true } });
            Assert.That(reasons, Is.Empty);
        }
    }
}
