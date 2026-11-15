using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace NoiseRun.Tests
{
    public class FilePickerTests
    {
        string _folder;

        [SetUp]
        public void SetUp()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            _folder = Path.Combine(projectRoot, "Temp", "noiserun_tests", "songs_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
        }

        [TearDown]
        public void TearDown()
        {
            if (_folder != null && Directory.Exists(_folder)) Directory.Delete(_folder, true);
        }

        [Test]
        public void ListSongs_KeepsOnlyMp3WavOgg_CaseInsensitive()
        {
            foreach (string name in new[] { "a.mp3", "B.WAV", "c.ogg", "d.txt", "e.flac" })
                File.WriteAllBytes(Path.Combine(_folder, name), new byte[0]);

            string[] songs = FilePicker.ListSongs(_folder);

            Assert.AreEqual(3, songs.Length);
            string[] names = songs.Select(Path.GetFileName).ToArray();
            CollectionAssert.AreEquivalent(new[] { "a.mp3", "B.WAV", "c.ogg" }, names);
        }

        [Test]
        public void ListSongs_EmptyFolder_ReturnsEmpty()
        {
            Assert.AreEqual(0, FilePicker.ListSongs(_folder).Length);
        }

        [Test]
        public void SongsFolder_ReturnsExistingFolder()
        {
            string folder = FilePicker.SongsFolder;
            Assert.IsFalse(string.IsNullOrEmpty(folder));
            Assert.IsTrue(Directory.Exists(folder), folder);
        }
    }
}
