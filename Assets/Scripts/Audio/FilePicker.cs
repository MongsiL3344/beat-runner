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

        /// <summary>
        /// 게임에 넣어 둔 기본 곡 폴더: Application.streamingAssetsPath/Songs(프로젝트의 Assets/StreamingAssets/Songs, 빌드에서는 *_Data/StreamingAssets/Songs).
        /// 빌드에 원본 파일 그대로 복사되므로 사용자 곡과 같은 파일 경로로 불러온다. 사용자 결정(T8 뒤 제출 준비).
        /// </summary>
        public static string BuiltInSongsFolder => Application.streamingAssetsPath + "/Songs";

        /// <summary>곡 목록(F)에 보여 줄 전체 곡: 기본 곡(이름순)을 먼저, 그 뒤에 사용자 Songs 폴더 곡(이름순).</summary>
        public static string[] ListAllSongs()
        {
            string[] builtIn = ListSongs(BuiltInSongsFolder);
            string[] user = ListSongs(SongsFolder);
            var all = new string[builtIn.Length + user.Length];
            builtIn.CopyTo(all, 0);
            user.CopyTo(all, builtIn.Length);
            return all;
        }

        /// <summary>path가 기본 곡 폴더 안의 파일인지.</summary>
        public static bool IsBuiltIn(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            return string.Equals(dir, Path.GetFullPath(BuiltInSongsFolder), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>기본 곡의 화면 이름: 확장자를 빼고 '_'를 공백으로(예: "Pressao no Asfalto").</summary>
        public static string BuiltInDisplayName(string path)
        {
            return Path.GetFileNameWithoutExtension(path).Replace('_', ' ');
        }

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
