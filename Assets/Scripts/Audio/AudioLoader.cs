using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace NoiseRun
{
    /// <summary>파일 → AudioClip (구현명세 3.2 기본 경로).</summary>
    public static class AudioLoader
    {
        /// <summary>
        /// 성공하면 onLoaded를 1회, 실패하면 onError를 비어 있지 않은 메시지로 정확히 1회 호출한다.
        /// </summary>
        public static IEnumerator Load(string path, Action<AudioClip> onLoaded, Action<string> onError)
        {
            if (string.IsNullOrEmpty(path))
            {
                onError?.Invoke("파일 경로가 비어 있습니다");
                yield break;
            }

            AudioType type = GuessAudioType(path);
            if (type == AudioType.UNKNOWN)
            {
                string ext = Path.GetExtension(path);
                onError?.Invoke("지원하지 않는 형식입니다(" + (string.IsNullOrEmpty(ext) ? "확장자 없음" : ext) + "). mp3/wav/ogg만 쓸 수 있습니다");
                yield break;
            }

            if (!File.Exists(path))
            {
                onError?.Invoke("파일이 없습니다");
                yield break;
            }

            AudioClip clip = null;
            string error = null;

            UnityWebRequest req = null;
            try
            {
                string uri = new Uri(Path.GetFullPath(path)).AbsoluteUri;
                req = UnityWebRequestMultimedia.GetAudioClip(uri, type);
                var dh = (DownloadHandlerAudioClip)req.downloadHandler;
                dh.streamAudio = false;
                dh.compressed = false;
            }
            catch (Exception e)
            {
                req?.Dispose();
                req = null;
                error = string.IsNullOrEmpty(e.Message) ? "읽기 요청을 만들지 못했습니다" : e.Message;
            }

            if (req != null)
            {
                using (req)
                {
                    yield return req.SendWebRequest();

                    if (req.result != UnityWebRequest.Result.Success)
                    {
                        error = string.IsNullOrEmpty(req.error) ? "읽기 요청에 실패했습니다" : req.error;
                    }
                    else
                    {
                        try
                        {
                            clip = DownloadHandlerAudioClip.GetContent(req);
                        }
                        catch (Exception e)
                        {
                            error = string.IsNullOrEmpty(e.Message) ? "오디오를 디코딩하지 못했습니다" : e.Message;
                        }
                        if (error == null)
                        {
                            if (clip == null) error = "오디오를 디코딩하지 못했습니다";
                            else if (clip.length <= 0f) error = "오디오 길이가 0입니다";
                        }
                    }
                }
            }

            if (error != null)
            {
                if (clip != null) UnityEngine.Object.Destroy(clip);
                onError?.Invoke(error);
                yield break;
            }

            clip.name = Path.GetFileName(path);
            onLoaded?.Invoke(clip);
        }

        /// <summary>확장자로 AudioType을 정한다. 모르는 확장자는 UNKNOWN.</summary>
        public static AudioType GuessAudioType(string path)
        {
            if (string.IsNullOrEmpty(path)) return AudioType.UNKNOWN;
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".mp3": return AudioType.MPEG;
                case ".wav": return AudioType.WAV;
                case ".ogg": return AudioType.OGGVORBIS;
                default: return AudioType.UNKNOWN;
            }
        }

        /// <summary>클립의 PCM 전체를 인터리브 float 배열로 읽는다(메인 스레드).</summary>
        public static float[] ReadInterleaved(AudioClip clip)
        {
            var data = new float[clip.samples * clip.channels];
            if (!clip.GetData(data, 0))
                throw new InvalidOperationException("오디오 PCM을 읽을 수 없습니다");
            return data;
        }
    }
}
