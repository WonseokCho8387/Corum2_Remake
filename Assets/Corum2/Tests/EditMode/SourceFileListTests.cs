using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Corum2.Core;
using NUnit.Framework;

namespace Corum2.Tests
{
    /// <summary>WP02-M02: per-file path/size/SHA-256 list for master 13.3 source_file_hash_manifest.
    /// The run-time list and the git link are checked end to end by Tools/validate_manifest.py.</summary>
    public class SourceFileListTests
    {
        string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "corum2-srclist-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_root, "Assets", "Sub"));
            File.WriteAllText(Path.Combine(_root, "Assets", "a.txt"), "abc", new UTF8Encoding(false));
            File.WriteAllBytes(Path.Combine(_root, "Assets", "Sub", "b.bin"), new byte[] { 0, 1, 2, 255 });
            File.WriteAllText(Path.Combine(_root, "Assets", "Sub", "한글.cs"), "// x\r\n", new UTF8Encoding(false));
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_root, true); } catch (IOException) { }
        }

        [Test]
        public void Sha256_KnownVector()
        {
            Assert.That(SourceFileList.Sha256Hex(Encoding.ASCII.GetBytes("abc")),
                Is.EqualTo("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"));
        }

        [Test]
        public void Hash_SizesHashesSortedAndMissing()
        {
            var missing = new List<string>();
            var list = SourceFileList.Hash(_root, new[] { "Assets/Sub/b.bin", "Assets\\a.txt", "Assets/gone.cs", "Assets/a.txt", "Assets/Sub/한글.cs" }, missing);
            Assert.That(list.Count, Is.EqualTo(3), "duplicates collapse, missing files are not rows");
            Assert.That(list[0].Path, Is.EqualTo("Assets/Sub/b.bin"));
            Assert.That(list[1].Path, Is.EqualTo("Assets/Sub/한글.cs"));
            Assert.That(list[2].Path, Is.EqualTo("Assets/a.txt"));
            Assert.That(list[2].Size, Is.EqualTo(3));
            Assert.That(list[2].Sha256, Is.EqualTo("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"));
            Assert.That(list[0].Size, Is.EqualTo(4));
            Assert.That(list[1].Size, Is.EqualTo(6), "bytes as on disk, CRLF kept");
            Assert.That(missing, Is.EqualTo(new[] { "Assets/gone.cs" }));
        }

        [Test]
        public void Tsv_RoundTrips()
        {
            var list = SourceFileList.Hash(_root, SourceFileList.Walk(_root, new[] { "Assets", "NoSuchFolder" }));
            list[0].GitBlob = "0123456789abcdef0123456789abcdef01234567";
            var tsv = SourceFileList.ToTsv(list, new[] { new KeyValuePair<string, string>("run_id", "RUN-1") });
            StringAssert.StartsWith("# run_id: RUN-1\n# entries: 3\n# total_bytes: 13\n" + SourceFileList.Columns + "\n", tsv);
            var back = SourceFileList.Parse(tsv);
            Assert.That(back.Count, Is.EqualTo(3));
            for (var i = 0; i < 3; i++)
            {
                Assert.That(back[i].Path, Is.EqualTo(list[i].Path));
                Assert.That(back[i].Size, Is.EqualTo(list[i].Size));
                Assert.That(back[i].Sha256, Is.EqualTo(list[i].Sha256));
                Assert.That(back[i].GitBlob, Is.EqualTo(list[i].GitBlob));
            }
        }

        [Test]
        public void Paths_MustBeRelativeAndSingleLine()
        {
            Assert.Throws<ArgumentException>(() => SourceFileList.Hash(_root, new[] { "../outside.txt" }));
            Assert.Throws<ArgumentException>(() => SourceFileList.Hash(_root, new[] { "/abs.txt" }));
            Assert.Throws<ArgumentException>(() => SourceFileList.Hash(_root, new[] { "Assets/a\tb.txt" }));
        }

        [Test]
        public void Parse_RejectsBadRows()
        {
            Assert.Throws<FormatException>(() => SourceFileList.Parse("# x: y\n"));
            Assert.Throws<FormatException>(() => SourceFileList.Parse(SourceFileList.Columns + "\nonly\tthree\tcols\n"));
        }
    }
}
