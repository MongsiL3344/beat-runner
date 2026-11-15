using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using System.IO;
#endif

namespace NoiseRun
{
    /// <summary>
    /// 개발·검증용 옵션. Editor에서만 &lt;프로젝트&gt;/Temp/noiserun_dev.txt(줄마다 키=값)를 읽는다.
    /// 빌드에서는 파일을 읽지 않고 모두 기본값이다. T1은 file 키만 인식한다.
    /// </summary>
    public static class DevOptions
    {
        static readonly HashSet<string> KnownKeys = new HashSet<string> { "file" };

        /// <summary>Load 후 file 키 값. 없으면 null.</summary>
        public static string AutoFile { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            AutoFile = null;
        }

        /// <summary>옵션 파일을 읽는다. 파일을 읽었으면 true. 빌드에서는 항상 false.</summary>
        public static bool Load()
        {
            AutoFile = null;
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
            }
            return true;
#else
            return false;
#endif
        }
    }
}
