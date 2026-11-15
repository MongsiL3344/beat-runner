using System;
using System.Collections.Generic;
using System.IO;
using SFB;
using UnityEngine;

namespace NoiseRun
{
    /// <summary>네이티브 파일 선택창(SFB)과 Songs 폴더 대안.</summary>
    public static class FilePicker
    {
        static readonly string[] SongExtensions = { "mp3", "wav", "ogg" };

        /// <summary>파일 선택창을 연다. 경로 또는 취소 시 null. 플러그인 실패 예외는 그대로 던진다.</summary>
        public static string OpenDialog()
        {
            var filters = new[] { new ExtensionFilter("Audio", SongExtensions) };
            string[] paths = StandaloneFileBrowser.OpenFilePanel("음악 파일 선택", "", filters, false);
            return (paths != null && paths.Length > 0 && !string.IsNullOrEmpty(paths[0])) ? paths[0] : null;
        }

        /// <summary>Application.persistentDataPath/Songs. 없으면 만든다.</summary>
        public static string SongsFolder
        {
            get
            {
                string folder = Application.persistentDataPath + "/Songs";
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                return folder;
            }
        }

        public static string[] ListSongs() => ListSongs(SongsFolder);

        /// <summary>folder 안의 .mp3/.wav/.ogg 전체 경로. 확장자 대소문자 무시, 파일 이름순.</summary>
        public static string[] ListSongs(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return Array.Empty<string>();

            var result = new List<string>();
            foreach (string file in Directory.GetFiles(folder))
            {
                string ext = Path.GetExtension(file);
                if (ext.Length < 2) continue;
                ext = ext.Substring(1);
                foreach (string allowed in SongExtensions)
                {
                    if (string.Equals(ext, allowed, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(file);
                        break;
                    }
                }
            }
            result.Sort((a, b) => string.Compare(Path.GetFileName(a), Path.GetFileName(b), StringComparison.OrdinalIgnoreCase));
            return result.ToArray();
        }
    }
}
