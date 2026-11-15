using System.IO;
using UnityEngine;
using UnityEngine.TestTools;

namespace NoiseRun.Tests
{
    /// <summary>
    /// Moves Temp/noiserun_dev.txt aside before the PlayMode run so Bootstrap AutoStart is not
    /// affected by dev options, and restores it afterwards. Does nothing if the file did not exist.
    /// </summary>
    public sealed class DevFileGuard : IPrebuildSetup, IPostBuildCleanup
    {
#if UNITY_EDITOR
        static string DevPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "noiserun_dev.txt"));
        static string BackupPath => DevPath + ".testbak";
#endif

        public void Setup()
        {
#if UNITY_EDITOR
            if (!File.Exists(DevPath)) return;
            if (File.Exists(BackupPath)) File.Delete(BackupPath); // leftover from an aborted run; keep the current file
            File.Move(DevPath, BackupPath);
#endif
        }

        public void Cleanup()
        {
#if UNITY_EDITOR
            if (!File.Exists(BackupPath)) return;
            if (File.Exists(DevPath)) File.Delete(DevPath);
            File.Move(BackupPath, DevPath);
#endif
        }
    }
}
