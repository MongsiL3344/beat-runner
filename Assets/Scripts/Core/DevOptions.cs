using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using System.Globalization;
using System.IO;
#endif

namespace NoiseRun
{
    /// <summary>
    /// 개발·검증용 옵션. Editor에서만 &lt;프로젝트&gt;/Temp/noiserun_dev.txt(줄마다 키=값)를 읽는다.
    /// 빌드에서는 파일을 읽지 않고 모두 기본값(null, false)이다. 인식하는 키: file, startAt, freezeAt, gallery, autoPlay, noCatch.
    /// </summary>
    public static class DevOptions
    {
        static readonly HashSet<string> KnownKeys = new HashSet<string> { "file", "startAt", "freezeAt", "gallery", "autoPlay", "noCatch" };
        static readonly HashSet<string> StartAtValues = new HashSet<string> { "title", "preview", "playing", "debug" };

        /// <summary>Load 후 file 키 값. 없으면 null.</summary>
        public static string AutoFile { get; private set; }

        /// <summary>startAt 키 값(title, preview, playing, debug). 없거나 모르는 값이면 null.</summary>
        public static string StartAt { get; private set; }

        /// <summary>freezeAt 키 값(곡 시간 고정, 초). 없거나 숫자가 아니면 null.</summary>
        public static double? FreezeAt { get; private set; }

        /// <summary>gallery 키 값(true면 T4 스프라이트 갤러리를 띄운다). 없거나 true/false가 아니면 false.</summary>
        public static bool Gallery { get; private set; }

        /// <summary>autoPlay 키 값(true면 Playing에서 노트 시각마다 정답 동작을 자동 실행). 없거나 true/false가 아니면 false.</summary>
        public static bool AutoPlay { get; private set; }

        /// <summary>noCatch 키 값(true면 추격자가 잡지 않음. Bootstrap이 ChaserController.NoCatch에 넣는다). 없거나 true/false가 아니면 false.</summary>
        public static bool NoCatch { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            AutoFile = null;
            StartAt = null;
            FreezeAt = null;
            Gallery = false;
            AutoPlay = false;
            NoCatch = false;
        }

        /// <summary>옵션 파일을 읽는다. 파일을 읽었으면 true. 빌드에서는 항상 false.</summary>
        public static bool Load()
        {
            AutoFile = null;
            StartAt = null;
            FreezeAt = null;
            Gallery = false;
            AutoPlay = false;
            NoCatch = false;
#if UNITY_EDITOR
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "noiserun_dev.txt"));
            if (!File.Exists(path)) return false;

            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    Debug.LogWarning("DevOptions: '키=값' 형식이 아닌 줄을 무시합니다: " + line);
                    continue;
                }

                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                if (!KnownKeys.Contains(key))
                {
                    Debug.LogWarning("DevOptions: 알 수 없는 키 '" + key + "'");
                    continue;
                }

                if (key == "file") AutoFile = value.Length == 0 ? null : value;
                else if (key == "startAt") StartAt = ParseStartAt(value);
                else if (key == "freezeAt") FreezeAt = ParseFreezeAt(value);
                else if (key == "gallery") Gallery = ParseBool(key, value);
                else if (key == "autoPlay") AutoPlay = ParseBool(key, value);
                else if (key == "noCatch") NoCatch = ParseBool(key, value);
            }
            return true;
#else
            return false;
#endif
        }

#if UNITY_EDITOR
        static string ParseStartAt(string value)
        {
            if (value.Length == 0) return null;
            if (StartAtValues.Contains(value)) return value;
            Debug.LogWarning("DevOptions: startAt 값 '" + value + "' 을(를) 알 수 없습니다(title, preview, playing, debug)");
            return null;
        }

        static double? ParseFreezeAt(string value)
        {
            if (value.Length == 0) return null;
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
                && !double.IsNaN(seconds) && !double.IsInfinity(seconds))
                return seconds;
            Debug.LogWarning("DevOptions: freezeAt 값 '" + value + "' 은(는) 숫자가 아닙니다");
            return null;
        }

        static bool ParseBool(string key, string value)
        {
            if (value == "true") return true;
            if (value == "false") return false;
            Debug.LogWarning("DevOptions: " + key + " 값 '" + value + "' 은(는) true/false가 아닙니다");
            return false;
        }
#endif
    }
}
