using UnityEngine;
using UnityEngine.UI;

namespace NoiseRun
{
    /// <summary>
    /// 자동 시작, 카메라·Canvas·Conductor·레벨·주인공·카메라 연출·GameFlow 생성. 만든 오브젝트는 모두 이 GameObject 아래에 두어
    /// Restart 때 한꺼번에 제거된다(씬의 Main Camera는 건드리지 않는다).
    /// </summary>
    public sealed class Bootstrap : MonoBehaviour
    {
        public static Bootstrap Instance { get; private set; }

        public GameFlow Flow { get; private set; }
        public Conductor Conductor { get; private set; }
        public Canvas Canvas { get; private set; }
        public LevelBuilder Level { get; private set; }
        public PlayerController Player { get; private set; }
        public CameraDirector CameraDirector { get; private set; }

        MenuScreens menus;
        AnalysisDebugView debugView;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            if (Instance != null || FindAnyObjectByType<Bootstrap>() != null) return; // 중복 방지

            var b = Create(null);
            if (b == null) return;

            // 개발 옵션은 AutoStart 경로에서만 적용한다(Restart는 적용하지 않음).
            DevOptions.Load();
            ApplyDevOptions(b);
        }

        static void ApplyDevOptions(Bootstrap b)
        {
            // startAt: debug는 T3 분석 디버그 화면, playing(기본)은 분석 직후 Playing. title·preview는 T8에서 지원한다.
            string startAt = DevOptions.StartAt;
            if (startAt == "title" || startAt == "preview")
                Debug.LogWarning("DevOptions: startAt=" + startAt + " 은(는) 아직 지원하지 않습니다. 기본 동작(분석 후 Playing)으로 진행합니다.");
            b.Flow.DebugPreview = startAt == "debug";
            b.Flow.AutoPlay = DevOptions.AutoPlay;

            double? freezeAt = DevOptions.FreezeAt;
            if (freezeAt.HasValue)
            {
                b.Conductor.Clock = new FakeSongClock { Time = freezeAt.Value };
                b.Conductor.FrozenSongTime = freezeAt;
            }

#if UNITY_EDITOR
            // 갤러리가 켜져 있으면 메뉴를 숨기고 스프라이트를 나열한다. 파일 자동 시작은 하지 않는다.
            // Flow를 꺼서 보이지 않는 메뉴가 Enter 등 입력으로 넘어가지 않게 한다.
            if (DevOptions.Gallery)
            {
                b.Canvas.gameObject.SetActive(false);
                b.Flow.enabled = false;
                SpriteGallery.Create(b.transform);
                return;
            }
#endif

            string file = DevOptions.AutoFile;
            if (!string.IsNullOrEmpty(file)) b.Flow.StartFromFile(file);
        }

        /// <summary>
        /// 기존 Bootstrap과 그것이 만든 오브젝트를 즉시 제거하고 새로 만든다. 테스트용.
        /// clockOrNull이 null이면 DspSongClock을 쓴다. 반환 직후 Instance·Flow를 쓸 수 있고 Flow.State == Title이다.
        /// </summary>
        public static Bootstrap Restart(ISongClock clockOrNull)
        {
            foreach (var old in FindObjectsByType<Bootstrap>(FindObjectsInactive.Include))
                DestroyImmediate(old.gameObject);
            Instance = null;
            return Create(clockOrNull);
        }

        static Bootstrap Create(ISongClock clockOrNull)
        {
            var go = new GameObject("Bootstrap");
            var b = go.AddComponent<Bootstrap>(); // Awake에서 시스템을 만든다
            if (Instance != b) return null;
            if (clockOrNull != null) b.Conductor.Clock = clockOrNull;
            return b;
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            Camera cam = SetupCamera();
            Canvas = CreateCanvas();
            Conductor = CreateConductor();

            // 레벨·주인공·카메라 연출은 이 GameObject 아래에 만들어 Restart 때 함께 제거된다.
            // CameraDirector는 별도 오브젝트라 씬 카메라에는 컴포넌트가 붙지 않는다. 주인공은 Playing에 들어갈 때만 보인다.
            Level = LevelBuilder.Create(transform);
            Player = PlayerController.Create(transform);
            Player.gameObject.SetActive(false);
            CameraDirector = CameraDirector.Create(cam, transform);

            menus = new MenuScreens(Canvas.transform);
            debugView = AnalysisDebugView.Create(Canvas.transform);
            Flow = gameObject.AddComponent<GameFlow>();
            Flow.Init(Conductor, menus, debugView, Level, Player, CameraDirector);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        Camera SetupCamera()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                camGo.transform.SetParent(transform, false);
                camGo.transform.position = new Vector3(0f, 0f, GameConfig.CameraZ);
                cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                if (FindAnyObjectByType<AudioListener>() == null) camGo.AddComponent<AudioListener>();
            }
            cam.orthographic = true;
            cam.orthographicSize = GameConfig.CameraOrthoSize;
            return cam;
        }

        Canvas CreateCanvas()
        {
            var canvasGo = new GameObject("Canvas");
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = GameConfig.RefResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        Conductor CreateConductor()
        {
            var go = new GameObject("Conductor");
            go.transform.SetParent(transform, false);
            return go.AddComponent<Conductor>();
        }
    }
}
