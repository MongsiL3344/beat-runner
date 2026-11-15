# beat-runner 에이전트 작업 규칙

이 파일은 Unity 프로젝트 루트(맥 `~/Dev/beat-runner`)에 둔다. 게임 기획·구현 명세는 `docs/기획서.md`, `docs/구현명세.md`를 따른다.

## 작업 진행 기록

- 세션을 시작하면 먼저 `docs/작업체크리스트.md`를 읽고, "지금 할 일"과 "다음 작업에 넘길 메모"를 확인한 뒤 작업한다.
- 작업(T1~T15)이 끝나면 같은 파일을 갱신한다: 진행표 체크, "지금 할 일"을 다음 작업으로 변경, 기능 ID 상태 갱신, 다음 작업에 필요한 메모(주의점, 위험, 임시로 바꿔 둔 것) 추가. 사람 확인이 필요한 작업은 사용자의 확인 답을 받은 뒤에 체크한다.

## 환경

- 에이전트는 Docker 컨테이너 안에서 동작한다. Unity Editor와 Unity CLI는 호스트 맥에 있다.
- 프로젝트 폴더: 맥 `~/Dev/beat-runner` = 컨테이너에 마운트된 이 파일이 있는 폴더. 같은 폴더다.
- Editor 조작은 공식 Unity CLI(`/opt/homebrew/bin/unity`)와 Unity Pipeline 패키지(`com.unity.pipeline`) 1.0.0으로 한다. MCP(`unity mcp`)는 쓰지 않는다.

## 역할 분리

| 작업 | 방법 |
|---|---|
| C# 코드, `docs/`, `Packages/manifest.json` 등 파일 수정 | 컨테이너에서 마운트된 프로젝트 폴더를 직접 수정. SSH로 맥에서 파일을 편집하지 않는다. |
| Editor 조작(상태 확인, 재컴파일, 콘솔, 플레이 모드, 스크린샷, 빌드) | SSH로 맥에서 `unity` 명령 실행 |

## 디렉터리 구조

기준은 `docs/구현명세.md` 4.2절(스크립트 목록)과 4.6절(어셈블리)이다. 새 파일은 명세 표에 적힌 경로에 만들고, 표에 없는 파일은 아래 규칙으로 위치를 정한다.

```
Assets/
├── Scripts/                      게임 코드 전체. NoiseRun.Runtime.asmdef (rootNamespace NoiseRun)
│   ├── Core/                     Bootstrap, GameFlow, GameConfig, DevOptions, SaveData
│   ├── Audio/                    FilePicker, AudioLoader, Conductor, ISongClock, SyncCalibration, SfxPlayer
│   ├── Analysis/                 Fft, OnsetDetector, BpmEstimator, BeatMapGenerator, BeatMap
│   ├── Gameplay/                 RhythmJudge, PlayerController, ChaserController, LevelBuilder
│   ├── View/                     UiFactory, CameraDirector, BeatBarUI, Hud, MenuScreens, ResultScreen, AnalysisDebugView
│   └── Art/                      PixelArt, SpriteData, SpriteAnimator
├── Tests/PlayMode/               NoiseRun.Tests.PlayMode.asmdef (rootNamespace NoiseRun.Tests)
│   ├── *Tests.cs                 테스트 파일은 이 폴더 바로 아래에 평평하게 둔다
│   ├── TestSupport/              테스트 헬퍼(TestAudio 등). 테스트 메서드는 두지 않는다
│   └── TestData/                 코드로 만들 수 없는 고정 입력 파일(ffmpeg로 변환한 mp3·ogg 등)
├── StandaloneFileBrowser/        외부 라이브러리 원본. 자체 asmdef. 수정하지 않는다
├── Plugins/NLayer/               (mp3 폴백이 필요할 때만) NLayer 소스 + NLayer.asmdef
├── Resources/Fonts/              Resources.Load로 읽는 폰트와 라이선스 파일
├── Scenes/                       SampleScene 하나만 쓴다
└── Settings/                     URP 템플릿 기본 설정. 건드리지 않는다
```

규칙

- **네임스페이스**: `Scripts/` 아래는 하위 폴더와 상관없이 모두 `namespace NoiseRun`, 테스트는 모두 `namespace NoiseRun.Tests`. 폴더별 네임스페이스(`NoiseRun.Audio` 등)를 만들지 않는다.
- **폴더 선택**: 클래스 하나는 4.2절의 역할(Core=시작·흐름·설정, Audio=파일·재생·시간, Analysis=분석·맵 데이터, Gameplay=판정·캐릭터·레벨, View=카메라·UI, Art=스프라이트 생성)로 폴더를 정한다. 새 최상위 폴더나 하위 폴더를 만들어야 하면 먼저 사용자에게 묻는다.
- **새 asmdef 금지**: 게임 코드는 `NoiseRun.Runtime` 하나로 묶는다. 외부 라이브러리를 추가할 때만 그 폴더에 asmdef를 두고 `NoiseRun.Runtime`의 references에 넣는다.
- **Editor 전용 코드**: 별도 `Editor/` 폴더를 만들지 않고 `#if UNITY_EDITOR`로 감싼다(`DevOptions` 방식). 에디터 확장이 꼭 필요해지면 먼저 사용자에게 묻는다.
- **에셋**: 스프라이트·효과음은 코드로 만들므로 이미지·사운드 에셋 폴더를 두지 않는다. `Resources/`에는 런타임에 이름으로 읽어야 하는 파일만 넣는다.
- **테스트 데이터**: 테스트 음원은 `TestSupport/TestAudio.cs`로 만드는 것이 기본이다. `TestData/`에는 코드로 만들 수 없는 파일만 넣고, 크기는 짧게(수 초) 유지한다.
- **임시 파일**: 테스트·검증 중 생기는 파일(wav, 스크린샷, `noiserun_dev.txt`)은 프로젝트의 `Temp/` 아래에만 쓴다. 테스트는 `Temp/noiserun_tests/`를 쓴다. `Temp/`는 git에서 제외된다.
- **문서**: 기획·명세·다이어그램은 `docs/`에 둔다.
- **.meta**: `Assets/` 아래 파일·폴더를 만들거나 옮기면 `.meta`도 함께 커밋한다. 옮길 때는 `.meta`도 같이 옮겨 GUID를 유지한다.

## 명령 형식

```bash
ssh wkdgustjr@host.docker.internal 'cd ~/Dev/beat-runner && /opt/homebrew/bin/unity command ...'
```

- 항상 `cd ~/Dev/beat-runner` 후 실행한다. CLI는 현재 디렉터리를 포함하는 프로젝트의 Editor를 찾는다.
- `unity`는 절대 경로로 부른다. 비대화형 SSH 셸에는 `/opt/homebrew/bin`이 PATH에 없을 수 있다.
- Editor가 여러 개 열려 있으면(`AMBIGUOUS_EDITOR`) `--project-path ~/Dev/beat-runner`를 붙인다.
- 인자에 따옴표가 겹치면(예: `script eval`) heredoc으로 넘긴다.

  ```bash
  ssh wkdgustjr@host.docker.internal bash -s <<'EOF'
  cd ~/Dev/beat-runner && /opt/homebrew/bin/unity command script eval "return Application.unityVersion;"
  EOF
  ```

## 이 CLI 버전의 차이

- `unity pipeline list`는 없다. Editor 연결·패키지 상태 확인은 `unity pipeline editors`로 한다.
- 명령 이름과 플래그는 CLI가 아니라 Editor 쪽 Pipeline 패키지가 정한다. 기억이나 다른 버전 문서로 추측하지 말고, 처음 쓰는 명령은 먼저 목록에서 확인한다.

  ```bash
  unity command                                 # 명령 그룹 목록
  unity command <그룹>                          # 그룹 안의 명령 (예: unity command editor.playmode)
  unity command --query <단어>                  # 검색 (예: --query screenshot)
  unity command --detail full --format json     # 전체 명령과 파라미터 스키마
  ```

## 자주 쓰는 흐름

아래 명령 이름은 Unity CLI 공식 문서 기준 예시다. 위 목록 명령으로 실제 이름과 플래그를 확인한 뒤 쓴다.

1. 연결 확인: `unity pipeline editors`, `unity status --until-ready`
2. 코드 수정 후 컴파일 확인: `unity command script recompile` → `unity command console get`으로 오류·경고 확인
3. 플레이 모드: `unity command editor.playmode start` / `unity command editor.playmode stop`, 상태는 `unity command editor status`
4. 화면 확인: `unity command capture game_view --output-path Temp/shot.png` (`--output-path`는 프로젝트 기준 상대 경로). 파일이 마운트 폴더의 `Temp/`에 생기므로 컨테이너에서 바로 열어 본다.
5. 빌드: `unity command --query build`로 빌드 명령을 찾아 쓴다.

## 검증 규칙 (AI가 직접 플레이 테스트)

사용자에게 "실행해 보고 알려주세요"를 먼저 요청하지 않는다. CLI로 확인할 수 있는 건 AI가 직접 확인한다.

1. **코드를 바꿀 때마다** 재컴파일 후 콘솔에 오류가 0개인지 확인한다.
2. **기능을 완료할 때마다** PlayMode 테스트를 실행한다. 테스트는 Unity Test Framework와 Input System의 `InputTestFixture`로 키 입력(Space/↓/→/Esc)을 흉내 낸다. 이 기능은 `Packages/manifest.json`에 `"testables": ["com.unity.inputsystem"]`가 있어야 쓸 수 있다.
   - 음악 분석은 BPM을 알고 있는 테스트 음원을 코드로 만들어서 검증한다. 확인 항목은 BPM 추정 오차, 반박자 정렬, 앞 3초 비움, 대역별 장애물 종류다.
   - 판정·추격자·화면 전환은 가짜 시계에 곡 시간을 넣어서 검증한다.
3. **화면이 바뀌는 작업은** 플레이 모드에서 해당 화면의 게임 뷰 스크린샷을 찍는다. 그 이미지를 직접 열어 `docs/기획서.md`의 내용과 대조한다.
4. **사람에게만 요청하는 것**은 아래 네 가지다. 요청할 때는 무엇을 보고 어떻게 답하면 되는지 구체적으로 적는다.
   - 음악과 판정의 싱크 체감, 재미·난이도 체감
   - 네이티브 파일 선택창 동작 (CLI로 조작할 수 없다. AI 테스트는 파일 경로를 직접 넘기는 디버그 진입점을 쓴다)
   - Windows PC에서 빌드를 실행해 보는 것
   - 최종 결정

## 평가 기준: AI 활용도 (25%)

대회 공지의 평가 기준 원문이다. 작업할 때 이 기준을 염두에 둔다.

> ① 목표 전달: 원하는 동작과 성공 기준 (자료: 요청, 공유한 기획·요구사항)
> ② 정보 제공: 현재 자료와 작업 조건 (자료: 입력 파일, 오류, 환경, 공유·읽기 기록)
> ③ 업무 위임: AI 담당 범위와 반환 지점 (자료: 지시, 승인한 계획, 작업 요청)
> ④ 결과 확인: 실제 결과 관찰과 요구사항 대조 (자료: 실행·검토 기록, 실제 출력, 확인 흔적)
> ⑤ 활용 결정: 확인 근거에 따른 결정과 반영 (자료: 결정, 후속 지시, 변경 이력, 제출 버전)

## 문제 해결

| 증상 | 대응 |
|---|---|
| `STATUS_PIPELINE_LOAD_PENDING` | Editor가 열리는 중이거나 패키지 로드 대기. `unity status --until-ready`로 기다린다. 계속되면 사용자에게 Unity 창을 한 번 클릭(포커스)해 달라고 요청한다. |
| Pipeline이 응답하지 않고 Editor가 Safe Mode | 컴파일 오류 때문이다. `ssh ... 'tail -n 200 ~/Library/Logs/Unity/Editor.log'`로 오류를 읽고, 마운트 폴더에서 코드를 고친다. |
| `EDITOR_NOT_RUNNING` 또는 연결 실패 | Editor가 꺼져 있다. 사용자에게 프로젝트를 Unity로 열어 달라고 요청한다. |
| `EDITOR_NOT_READY` | 플레이 모드 진입·스크립트 리로드 중. 같은 명령에 `--timeout 60`을 붙여 다시 실행한다. |
| `AMBIGUOUS_EDITOR` | `--project-path ~/Dev/beat-runner`를 붙인다. |
