using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NoiseRun.Tests
{
    /// <summary>
    /// T5: LevelBuilder(장애물 위치·높이·비상구·정리), PlayerController 동작 연출, CameraDirector 추적.
    /// 오디오 분석 없이 손으로 만든 BeatMap을 쓴다.
    /// </summary>
    [PrebuildSetup(typeof(DevFileGuard)), PostBuildCleanup(typeof(DevFileGuard))]
    public class ObstacleTests
    {
        const float Tolerance = 0.001f;
        const double SongLength = 20.0;
        const string ObstaclePrefix = "Obstacle ";
        const string GridTextureName = "PixelArt"; // PixelArt가 만드는 텍스처 이름
        const int MaxTexturesPerBuild = 20;        // 격자당 1개(메모 M1). 타일마다 만들면 수천 개가 된다
        const float ViewportTolerance = 0.005f;
        const int RestartRepeats = 5;
        const float AspectWide = 16f / 9f;

        GameObject root;

        [SetUp]
        public void CreateRoot()
        {
            root = new GameObject("ObstacleTestsRoot");
        }

        [TearDown]
        public void DestroyRoot()
        {
            if (root != null) Object.DestroyImmediate(root);
        }

        static BeatMap MakeMap(params (double time, ObstacleKind kind)[] notes)
        {
            var map = new BeatMap
            {
                bpm = 120f,
                firstBeatTime = 0.0,
                songLength = SongLength,
                difficulty = Difficulty.Normal,
                notes = new List<Note>(),
            };
            foreach (var (time, kind) in notes)
                map.notes.Add(new Note { time = time, kind = kind, slotIndex = map.NearestSlot(time) });
            return map;
        }

        static BeatMap DefaultMap()
        {
            return MakeMap(
                (3.0, ObstacleKind.Box),
                (3.5, ObstacleKind.Laundry),
                (4.0, ObstacleKind.Door),
                (6.25, ObstacleKind.Box),
                (19.0, ObstacleKind.Laundry));
        }

        static int CountObstacles(Component owner)
        {
            int count = 0;
            foreach (SpriteRenderer sr in owner.GetComponentsInChildren<SpriteRenderer>(false))
                if (sr.name.StartsWith(ObstaclePrefix)) count++;
            return count;
        }

        static int CountGridTextures()
        {
            int count = 0;
            foreach (Texture2D tex in Resources.FindObjectsOfTypeAll<Texture2D>())
                if (tex.name == GridTextureName) count++;
            return count;
        }

        // ── 장애물 위치 ──

        [Test]
        public void ObstacleX_IsNoteTimeTimesScrollSpeed()
        {
            LevelBuilder level = LevelBuilder.Create(root.transform);
            BeatMap map = DefaultMap();
            level.Build(map);

            for (int i = 0; i < map.notes.Count; i++)
            {
                Transform obstacle = level.ObstacleFor(i);
                Assert.IsNotNull(obstacle, "노트 " + i + "의 장애물이 없습니다");
                Assert.AreEqual((float)(map.notes[i].time * GameConfig.ScrollSpeed), obstacle.position.x, Tolerance, "노트 " + i);
                Assert.AreEqual(level.TimeToX(map.notes[i].time), obstacle.position.x, Tolerance);
            }
        }

        [Test]
        public void TimeToX_IsTimeTimesScrollSpeed()
        {
            LevelBuilder level = LevelBuilder.Create(root.transform);
            Assert.AreEqual(0f, level.TimeToX(0.0), Tolerance);
            Assert.AreEqual(12.5f * GameConfig.ScrollSpeed, level.TimeToX(12.5), Tolerance);
        }

        [Test]
        public void ObstacleHeights_BoxAndDoorOnFloor_LaundryHangsFromCeiling()
        {
            // 스프라이트 크기(구현명세 9.3, 픽셀): 택배 상자 높이 10, 방화문 높이 24, 빨래 밑변은 바닥 위 10
            // (슬라이드 8보다 높고 서 있는 키 16보다 낮다).
            const float BoxPx = 10f;
            const float DoorPx = 24f;
            const float LaundryBottomPx = 10f;
            float ppu = GameConfig.PixelsPerUnit;

            LevelBuilder level = LevelBuilder.Create(root.transform);
            BeatMap map = DefaultMap();
            level.Build(map);

            for (int i = 0; i < map.notes.Count; i++)
            {
                Transform obstacle = level.ObstacleFor(i);
                Bounds bounds = obstacle.GetComponent<SpriteRenderer>().bounds;
                switch (map.notes[i].kind)
                {
                    case ObstacleKind.Box:
                    case ObstacleKind.Door:
                        float heightPx = map.notes[i].kind == ObstacleKind.Box ? BoxPx : DoorPx;
                        Assert.AreEqual(GameConfig.FloorY, obstacle.position.y, Tolerance, "바닥 장애물 y, 노트 " + i);
                        Assert.AreEqual(GameConfig.FloorY, bounds.min.y, Tolerance, "바닥 장애물 밑변, 노트 " + i);
                        Assert.AreEqual(heightPx / ppu, bounds.size.y, Tolerance, "바닥 장애물 높이, 노트 " + i);
                        break;
                    case ObstacleKind.Laundry:
                        float ceilingY = GameConfig.FloorY + GameConfig.CeilingHeight;
                        Assert.AreEqual(ceilingY, obstacle.position.y, Tolerance, "빨래 y, 노트 " + i);
                        Assert.AreEqual(ceilingY, bounds.max.y, Tolerance, "빨래 윗변은 천장선, 노트 " + i);
                        Assert.AreEqual(GameConfig.FloorY + LaundryBottomPx / ppu, bounds.min.y, Tolerance, "빨래 밑변은 바닥 위 10px, 노트 " + i);
                        break;
                }
            }
        }

        [Test]
        public void Obstacles_AreDrawnBetweenCorridorAndPlayer()
        {
            LevelBuilder level = LevelBuilder.Create(root.transform);
            level.Build(DefaultMap());

            int order = level.ObstacleFor(0).GetComponent<SpriteRenderer>().sortingOrder;
            Assert.AreEqual(GameConfig.SortOrderObstacle, order);
            Assert.Greater(order, GameConfig.SortOrderCorridor);
            Assert.Less(order, GameConfig.SortOrderPlayer);
        }

        [Test]
        public void NoteWithoutObstacle_HasNoObject()
        {
            LevelBuilder level = LevelBuilder.Create(root.transform);
            BeatMap map = MakeMap((3.0, ObstacleKind.Box), (4.0, ObstacleKind.None));
            level.Build(map);

            Assert.IsNotNull(level.ObstacleFor(0));
            Assert.IsNull(level.ObstacleFor(1));
            Assert.IsNull(level.ObstacleFor(2), "범위 밖 인덱스");
            Assert.IsNull(level.ObstacleFor(-1), "범위 밖 인덱스");
            Assert.AreEqual(1, CountObstacles(level));
        }

        // ── 비상구 ──

        [Test]
        public void ExitX_IsSongEndPlusDistance_AndExitObjectExistsBeyondSongEnd()
        {
            LevelBuilder level = LevelBuilder.Create(root.transform);
            BeatMap map = DefaultMap();
            level.Build(map);

            float songEndX = (float)(map.songLength * GameConfig.ScrollSpeed);
            Assert.AreEqual(songEndX + GameConfig.ExitDistanceAfterEnd, level.ExitX, Tolerance);

            Assert.IsNotNull(level.Exit, "비상구 오브젝트가 없습니다");
            Assert.AreEqual(level.ExitX, level.Exit.position.x, Tolerance);
            Assert.AreEqual(GameConfig.FloorY, level.Exit.GetComponent<SpriteRenderer>().bounds.min.y, Tolerance, "비상구는 바닥에 선다");
            Assert.Greater(level.Exit.GetComponent<SpriteRenderer>().bounds.min.x, songEndX, "비상구는 곡 끝 너머에 있어야 합니다");
            Assert.Greater(level.Exit.childCount, 0, "비상구 표지판이 없습니다");
        }

        [Test]
        public void Corridor_CoversWholeSongAndExit()
        {
            LevelBuilder level = LevelBuilder.Create(root.transform);
            level.Build(DefaultMap());

            float minX = float.MaxValue;
            float maxX = float.MinValue;
            foreach (SpriteRenderer sr in level.GetComponentsInChildren<SpriteRenderer>())
            {
                if (!sr.name.Contains("Floor")) continue;
                minX = Mathf.Min(minX, sr.bounds.min.x);
                maxX = Mathf.Max(maxX, sr.bounds.max.x);
            }
            float cameraLeftReach = GameConfig.PlayerViewportX * 2f * GameConfig.CameraOrthoSize * AspectWide;
            Assert.LessOrEqual(minX, -GameConfig.LeadInDistanceUnits - cameraLeftReach,
                "카운트다운 동안 달리는 시작 위치(x<0)와 그 왼쪽 화면 끝까지 복도가 있어야 합니다");
            Assert.GreaterOrEqual(maxX, level.ExitX, "비상구 아래까지 바닥이 이어져야 합니다");
        }

        // ── 정리 ──

        [UnityTest]
        public IEnumerator BuildTwice_LeavesOnlyTheSecondLevelsObstacles()
        {
            LevelBuilder level = LevelBuilder.Create(root.transform);
            level.Build(DefaultMap());
            BeatMap second = MakeMap((5.0, ObstacleKind.Box), (6.0, ObstacleKind.Door));
            level.Build(second);
            yield return null; // Destroy는 프레임 끝에 반영된다

            Assert.AreEqual(second.notes.Count, CountObstacles(level), "첫 레벨의 장애물이 남았습니다");
            Assert.AreEqual(5.0f * GameConfig.ScrollSpeed, level.ObstacleFor(0).position.x, Tolerance);
            Assert.AreEqual(1, CountExitObjects(level), "비상구는 하나여야 합니다");
        }

        [UnityTest]
        public IEnumerator Clear_RemovesObstaclesAndExit()
        {
            LevelBuilder level = LevelBuilder.Create(root.transform);
            level.Build(DefaultMap());
            level.Clear();
            yield return null;

            Assert.AreEqual(0, CountObstacles(level));
            Assert.AreEqual(0, CountExitObjects(level));
            Assert.AreEqual(0, level.GetComponentsInChildren<SpriteRenderer>().Length, "남은 렌더러");
            Assert.IsNull(level.ObstacleFor(0));
            Assert.IsNull(level.Exit);
        }

        [UnityTest]
        public IEnumerator BuildAndClear_DoNotLeakTextures_AndShareOneSpritePerGrid()
        {
            yield return null; // 이전 테스트에서 미뤄진 Destroy를 먼저 처리한다
            int baseline = CountGridTextures();
            LevelBuilder level = LevelBuilder.Create(root.transform);

            level.Build(DefaultMap());
            int whileBuilt = CountGridTextures() - baseline;
            Assert.Greater(whileBuilt, 0);
            Assert.LessOrEqual(whileBuilt, MaxTexturesPerBuild, "격자당 텍스처 1개만 만들어야 합니다(타일마다 만들면 수천 개)");

            for (int i = 0; i < 3; i++) level.Build(DefaultMap()); // 반복 빌드도 누적되면 안 된다
            level.Clear();
            yield return null;

            Assert.AreEqual(baseline, CountGridTextures(), "Clear 뒤에 텍스처가 남았습니다");
        }

        [UnityTest]
        public IEnumerator Restart_RepeatedlyDoesNotGrowTextureCount()
        {
            Bootstrap.Restart(new FakeSongClock());
            yield return null;
            int baseline = CountGridTextures();

            for (int i = 0; i < RestartRepeats; i++)
            {
                Bootstrap.Restart(new FakeSongClock());
                yield return null; // Destroy는 프레임 끝에 반영된다
            }

            Assert.LessOrEqual(CountGridTextures(), baseline, "Restart를 반복하면 텍스처가 쌓입니다");
            Bootstrap.Restart(null);
        }

        [UnityTest]
        public IEnumerator DestroyingTheBuilder_ReleasesTextures()
        {
            yield return null; // 이전 테스트에서 미뤄진 Destroy를 먼저 처리한다
            int baseline = CountGridTextures();
            LevelBuilder level = LevelBuilder.Create(root.transform);
            level.Build(DefaultMap());
            Object.Destroy(level.gameObject);
            yield return null;

            Assert.AreEqual(baseline, CountGridTextures());
        }

        static int CountExitObjects(Component owner)
        {
            int count = 0;
            foreach (SpriteRenderer sr in owner.GetComponentsInChildren<SpriteRenderer>(false))
                if (sr.name == "Exit") count++;
            return count;
        }

        // ── 주인공 ──

        [Test]
        public void ActionDuration_IsMinOfMaxSecAndHalfBeatTimesFactor()
        {
            Assert.AreEqual(GameConfig.ActionMaxSec, PlayerController.ActionDuration(1.0), Tolerance);
            double halfBeat = 0.2;
            Assert.AreEqual((float)(halfBeat * GameConfig.ActionBeatFactor), PlayerController.ActionDuration(halfBeat), Tolerance);
        }

        [Test]
        public void Player_StartsOnFloorWithoutAction_AndSetXMovesOnlyX()
        {
            PlayerController player = PlayerController.Create(root.transform);
            player.SetX(12.5f);
            Assert.IsNull(player.CurrentAction);
            Assert.AreEqual(12.5f, player.transform.position.x, Tolerance);
            Assert.AreEqual(GameConfig.FloorY, player.transform.position.y, Tolerance);
        }

        [Test]
        public void Jump_IsClearlyAirborneShortlyAfterStart_PeaksAtJumpHeight_AndLandsAfterDuration()
        {
            PlayerController player = PlayerController.Create(root.transform);
            player.SetRunning(true);
            float duration = PlayerController.ActionDuration(0.25);

            player.DoAction(ActionType.Jump, duration);
            Assert.AreEqual(ActionType.Jump, player.CurrentAction);

            float boxHeight = 10f / GameConfig.PixelsPerUnit; // 택배 상자(9.3절)
            player.Advance(duration * 0.1f);
            Assert.Greater(player.transform.position.y, boxHeight, "동작 직후 상자를 넘을 만큼 떠 있어야 합니다");

            float peak = 0f;
            const int Steps = 40;
            for (int i = 0; i < Steps - 10; i++) // 동작 시간의 85%까지만 진행(아직 공중)
            {
                player.Advance(duration / Steps);
                peak = Mathf.Max(peak, player.transform.position.y);
            }
            Assert.AreEqual(GameConfig.FloorY + GameConfig.JumpHeight, peak, 0.05f);

            player.SetX(7f);
            Assert.Greater(player.transform.position.y, GameConfig.FloorY, "점프 중 SetX는 높이를 유지합니다");

            player.Advance(duration);
            Assert.IsNull(player.CurrentAction);
            Assert.AreEqual(GameConfig.FloorY, player.transform.position.y, Tolerance);
        }

        [Test]
        public void SlideAndWave_StayOnFloorAndEndAfterDuration_WaveShowsSoundWaveEffect()
        {
            PlayerController player = PlayerController.Create(root.transform);
            SpriteRenderer wave = player.transform.Find("SoundWave").GetComponent<SpriteRenderer>();
            Assert.IsFalse(wave.gameObject.activeSelf);

            player.DoAction(ActionType.Slide, 0.3f);
            player.Advance(0.1f);
            Assert.AreEqual(ActionType.Slide, player.CurrentAction);
            Assert.AreEqual(GameConfig.FloorY, player.transform.position.y, Tolerance);
            Assert.IsFalse(wave.gameObject.activeSelf);
            player.Advance(0.3f);
            Assert.IsNull(player.CurrentAction);

            player.DoAction(ActionType.Wave, 0.3f);
            Assert.AreEqual(ActionType.Wave, player.CurrentAction);
            Assert.IsTrue(wave.gameObject.activeSelf, "음파 이펙트가 보여야 합니다");
            Assert.AreEqual(GameConfig.SortOrderEffect, wave.sortingOrder);
            Assert.Greater(wave.transform.position.x, player.transform.position.x, "음파는 주인공 앞(오른쪽)");
            player.Advance(0.5f);
            Assert.IsNull(player.CurrentAction);
            Assert.IsFalse(wave.gameObject.activeSelf);
        }

        [Test]
        public void Player_ReturnsToRunningOrIdle_AfterAction_AndFaceFlipsSprite()
        {
            PlayerController player = PlayerController.Create(root.transform);
            SpriteRenderer body = player.GetComponent<SpriteRenderer>();
            Assert.AreEqual(GameConfig.SortOrderPlayer, body.sortingOrder);

            player.Face(false);
            Assert.IsTrue(body.flipX);
            player.Face(true);
            Assert.IsFalse(body.flipX);

            // 달리기·대기 프레임을 미리 알아 둔다(애니메이터는 시작할 때 첫 프레임을 보여 준다).
            player.SetRunning(true);
            Sprite runFrame = body.sprite;
            player.SetRunning(false);
            Sprite idleFrame = body.sprite;
            Assert.AreNotEqual(idleFrame, runFrame);

            // 동작 중에 SetRunning을 바꿔도 동작은 유지되고, 끝난 뒤 반영된다.
            player.DoAction(ActionType.Jump, 0.3f);
            player.SetRunning(true);
            Assert.AreEqual(ActionType.Jump, player.CurrentAction);
            Sprite duringJump = body.sprite;
            player.Advance(0.5f);
            Assert.IsNull(player.CurrentAction);
            Assert.AreNotEqual(duringJump, body.sprite, "점프가 끝나면 달리기 프레임으로 돌아가야 합니다");
            Assert.AreNotEqual(idleFrame, body.sprite, "달리는 중이면 대기 프레임이 아니라 달리기 프레임이어야 합니다");

            // 달리기를 처음 시작할 때 보이는 프레임(달리기 프레임 목록의 첫 장)과 같아야 한다.
            Assert.AreEqual(runFrame, body.sprite, "달리기 프레임 중 하나여야 합니다");
        }

        // ── 카메라 ──

        [Test]
        public void Follow_PlacesTargetAtPlayerViewportX_SnapsToPixelGrid_AndDoesNotTouchTheSceneCamera()
        {
            var camGo = new GameObject("TestCamera");
            camGo.transform.SetParent(root.transform, false);
            camGo.transform.position = new Vector3(0f, 0f, GameConfig.CameraZ);
            Camera cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = GameConfig.CameraOrthoSize;
            cam.aspect = AspectWide;

            var target = new GameObject("Target").transform;
            target.SetParent(root.transform, false);
            target.position = new Vector3(37.3f, 0f, 0f);

            CameraDirector director = CameraDirector.Create(cam, root.transform);
            director.Follow(target);

            Assert.AreEqual(0, camGo.GetComponents<MonoBehaviour>().Length, "씬 카메라에 컴포넌트를 붙이면 안 됩니다");
            Assert.AreNotSame(camGo, director.gameObject);

            Assert.AreEqual(GameConfig.PlayerViewportX, cam.WorldToViewportPoint(target.position).x, ViewportTolerance);
            Assert.AreEqual(GameConfig.CameraZ, cam.transform.position.z, Tolerance);
            Assert.AreEqual(GameConfig.CameraY, cam.transform.position.y, Tolerance);
            Assert.AreEqual(GameConfig.PlayCameraOrthoSize, cam.orthographicSize, Tolerance, "추적을 시작하면 플레이 줌으로 바뀌어야 합니다");

            float ppu = GameConfig.PixelsPerUnit;
            float offsetPx = (cam.transform.position.x - target.position.x) * ppu;
            Assert.AreEqual(0f, offsetPx - Mathf.Round(offsetPx), Tolerance, "카메라와 대상의 거리가 도트 격자 위에 있어야 합니다");

            // 대상이 움직이면 따라가고, null이면 멈춘다.
            target.position = new Vector3(80f, 0f, 0f);
            director.Follow(target);
            Assert.AreEqual(GameConfig.PlayerViewportX, cam.WorldToViewportPoint(target.position).x, ViewportTolerance);

            Vector3 stopped = cam.transform.position;
            director.Follow(null);
            target.position = new Vector3(120f, 0f, 0f);
            Assert.AreEqual(stopped, cam.transform.position);
        }

        [Test]
        public void Follow_KeepsTargetAtSameScreenPosition_ForNonGridTargetPositions()
        {
            var camGo = new GameObject("TestCamera");
            camGo.transform.SetParent(root.transform, false);
            camGo.transform.position = new Vector3(0f, 0f, GameConfig.CameraZ);
            Camera cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = GameConfig.CameraOrthoSize;
            cam.aspect = AspectWide;

            var target = new GameObject("Target").transform;
            target.SetParent(root.transform, false);
            CameraDirector director = CameraDirector.Create(cam, root.transform);

            // 격자(1/PPU)에 맞지 않는 x 값들. 대상 화면 위치가 모두 똑같아야 한다(주인공이 ±½ 텍셀 떨리면 안 됨).
            float[] xs = { 10.013f, 10.027f, 10.0415f, 33.337f, 33.349f, 90.1234f };
            float first = 0f;
            for (int i = 0; i < xs.Length; i++)
            {
                target.position = new Vector3(xs[i], 0f, 0f);
                director.Follow(target);
                float viewportX = cam.WorldToViewportPoint(target.position).x;
                if (i == 0) first = viewportX;
                Assert.AreEqual(first, viewportX, 1e-4f, "x=" + xs[i]);
                Assert.AreEqual(GameConfig.PlayerViewportX, viewportX, ViewportTolerance, "x=" + xs[i]);
            }
        }

        [Test]
        public void ResetPose_CancelsActionHidesWaveAndReturnsToFloorIdle()
        {
            PlayerController player = PlayerController.Create(root.transform);
            SpriteRenderer body = player.GetComponent<SpriteRenderer>();
            SpriteRenderer wave = player.transform.Find("SoundWave").GetComponent<SpriteRenderer>();
            Sprite idle = body.sprite;
            player.SetRunning(true);

            player.DoAction(ActionType.Jump, 0.3f);
            player.Advance(0.1f);
            Assert.Greater(player.transform.position.y, GameConfig.FloorY);
            player.ResetPose();
            Assert.IsNull(player.CurrentAction);
            Assert.AreEqual(GameConfig.FloorY, player.transform.position.y, Tolerance);
            Assert.AreEqual(idle, body.sprite, "대기 프레임으로 돌아가야 합니다");

            player.DoAction(ActionType.Wave, 0.3f);
            Assert.IsTrue(wave.gameObject.activeSelf);
            player.ResetPose();
            Assert.IsFalse(wave.gameObject.activeSelf);
            Assert.IsNull(player.CurrentAction);
        }

        // ── 판정 반응: 방화문 날리기 (T6) ──

        const int BoxNote = 0;
        const int LaundryNote = 1;
        const int DoorNote = 2; // DefaultMap의 노트 순서

        [TestCase(Judgement.Perfect)]
        [TestCase(Judgement.Good)]
        public void SuccessfulWaveOnDoor_SwapsToBlownSprite_FliesRightAndUp_ThenDeactivates(Judgement judgement)
        {
            LevelBuilder level = LevelBuilder.Create(root.transform);
            level.Build(DefaultMap());
            Transform door = level.ObstacleFor(DoorNote);
            SpriteRenderer renderer = door.GetComponent<SpriteRenderer>();
            Sprite closed = renderer.sprite;
            Vector3 start = door.position;

            level.OnJudged(new JudgeEvent(judgement, DoorNote, 0.0, ActionType.Wave));

            Assert.AreNotEqual(closed, renderer.sprite, "부서진 문 스프라이트로 바뀌어야 합니다");
            Assert.IsTrue(door.gameObject.activeSelf, "날아가는 동안에는 보여야 합니다");

            level.Advance(GameConfig.DoorBlownSec / 2f);
            Assert.Greater(door.position.x, start.x, "오른쪽으로 날아가야 합니다");
            Assert.Greater(door.position.y, start.y, "위로 떠올라야 합니다");
            Assert.IsTrue(door.gameObject.activeSelf);

            level.Advance(GameConfig.DoorBlownSec);
            Assert.IsFalse(door.gameObject.activeSelf, "다 날아가면 비활성화되어야 합니다");
            Assert.AreEqual(start.x + GameConfig.DoorBlownDistanceUnits, door.position.x, Tolerance);
            Assert.AreEqual(start.y + GameConfig.DoorBlownRiseUnits, door.position.y, Tolerance);
            Assert.AreEqual(GameConfig.DoorBlownSpinDeg, Mathf.DeltaAngle(0f, door.eulerAngles.z), Tolerance);
        }

        [Test]
        public void MissOnDoor_AndSuccessOnBoxOrLaundry_LeaveObstaclesUntouched()
        {
            LevelBuilder level = LevelBuilder.Create(root.transform);
            level.Build(DefaultMap());
            int count = level.GetComponentsInChildren<SpriteRenderer>(true).Length;

            var events = new[]
            {
                new JudgeEvent(Judgement.Miss, DoorNote, 0.0, ActionType.Wave),
                new JudgeEvent(Judgement.Perfect, BoxNote, 0.0, ActionType.Jump),
                new JudgeEvent(Judgement.Good, LaundryNote, 0.0, ActionType.Slide),
            };
            int[] notes = { DoorNote, BoxNote, LaundryNote };

            for (int i = 0; i < events.Length; i++)
            {
                Transform obstacle = level.ObstacleFor(notes[i]);
                SpriteRenderer renderer = obstacle.GetComponent<SpriteRenderer>();
                Sprite sprite = renderer.sprite;
                Vector3 position = obstacle.position;

                level.OnJudged(events[i]);
                level.Advance(GameConfig.DoorBlownSec * 2f);

                Assert.AreEqual(sprite, renderer.sprite, "스프라이트가 바뀌면 안 됩니다, 이벤트 " + i);
                Assert.AreEqual(position, obstacle.position, "위치가 바뀌면 안 됩니다, 이벤트 " + i);
                Assert.IsTrue(obstacle.gameObject.activeSelf, "비활성화되면 안 됩니다, 이벤트 " + i);
            }
            Assert.AreEqual(count, level.GetComponentsInChildren<SpriteRenderer>(true).Length);
        }

        [Test]
        public void OnJudged_IgnoresNoteIndexOutOfRangeAndNonDoorJudgements()
        {
            LevelBuilder level = LevelBuilder.Create(root.transform);
            level.OnJudged(new JudgeEvent(Judgement.Perfect, DoorNote, 0.0, ActionType.Wave)); // Build 전
            level.Build(DefaultMap());
            Transform door = level.ObstacleFor(DoorNote);
            Vector3 start = door.position;

            level.OnJudged(new JudgeEvent(Judgement.OffBeat, -1, 0.0, ActionType.Wave));
            level.OnJudged(new JudgeEvent(Judgement.EmptyBeat, -1, 0.0, ActionType.Wave));
            level.OnJudged(new JudgeEvent(Judgement.Perfect, -1, 0.0, ActionType.Wave));
            level.OnJudged(new JudgeEvent(Judgement.Perfect, DefaultMap().notes.Count, 0.0, ActionType.Wave));
            level.Advance(GameConfig.DoorBlownSec * 2f);

            Assert.AreEqual(start, door.position);
            Assert.IsTrue(door.gameObject.activeSelf);
        }

        [Test]
        public void Clear_DropsFlyingDoors_AndAdvanceAfterwardsIsHarmless()
        {
            LevelBuilder level = LevelBuilder.Create(root.transform);
            level.Build(DefaultMap());
            level.OnJudged(new JudgeEvent(Judgement.Perfect, DoorNote, 0.0, ActionType.Wave));
            level.Clear();

            Assert.DoesNotThrow(() => level.Advance(GameConfig.DoorBlownSec));
            level.OnJudged(new JudgeEvent(Judgement.Perfect, DoorNote, 0.0, ActionType.Wave));
            Assert.IsNull(level.ObstacleFor(DoorNote));
        }

        // ── 판정 반응: 주인공 맞음·휘청 (T6) ──

        [Test]
        public void PlayHit_ShowsHitReactionOnFloor_AndReturnsToIdleAfterHitFlashSec()
        {
            PlayerController player = PlayerController.Create(root.transform);
            SpriteRenderer body = player.GetComponent<SpriteRenderer>();
            Sprite idle = body.sprite;
            Assert.AreEqual(PlayerController.Reaction.None, player.CurrentReaction);

            player.PlayHit();
            Assert.AreEqual(PlayerController.Reaction.Hit, player.CurrentReaction);
            Assert.IsNull(player.CurrentAction, "반응 중에는 동작이 없습니다");
            Assert.AreEqual(GameConfig.FloorY, player.transform.position.y, Tolerance);
            Assert.AreNotEqual(idle, body.sprite, "맞음 프레임을 보여야 합니다");

            player.Advance(GameConfig.HitFlashSec / 2f);
            Assert.AreEqual(PlayerController.Reaction.Hit, player.CurrentReaction, "아직 반응 시간 안입니다");

            player.Advance(GameConfig.HitFlashSec);
            Assert.AreEqual(PlayerController.Reaction.None, player.CurrentReaction);
            Assert.AreEqual(idle, body.sprite, "대기 프레임으로 돌아가야 합니다");
            Assert.AreEqual(GameConfig.FloorY, player.transform.position.y, Tolerance);
        }

        [Test]
        public void PlayStumble_ShowsStumbleReactionOnFloor_AndReturnsToRunAfterStumbleSec()
        {
            PlayerController player = PlayerController.Create(root.transform);
            SpriteRenderer body = player.GetComponent<SpriteRenderer>();
            player.SetRunning(true);
            Sprite runFrame = body.sprite;

            player.PlayStumble();
            Assert.AreEqual(PlayerController.Reaction.Stumble, player.CurrentReaction);
            Assert.IsNull(player.CurrentAction);
            Assert.AreEqual(GameConfig.FloorY, player.transform.position.y, Tolerance);
            Sprite stumbleFrame = body.sprite;
            Assert.AreNotEqual(runFrame, stumbleFrame, "휘청 프레임을 보여야 합니다");

            player.Advance(GameConfig.StumbleSec / 2f);
            Assert.AreEqual(PlayerController.Reaction.Stumble, player.CurrentReaction);
            Assert.AreEqual(stumbleFrame, body.sprite, "단일 프레임이 유지되어야 합니다");

            player.Advance(GameConfig.StumbleSec);
            Assert.AreEqual(PlayerController.Reaction.None, player.CurrentReaction);
            Assert.AreEqual(runFrame, body.sprite, "달리는 중이었으므로 달리기 프레임으로 돌아가야 합니다");

            player.PlayHit();
            Assert.AreNotEqual(stumbleFrame, body.sprite, "맞음과 휘청은 다른 프레임입니다");
        }

        [Test]
        public void PlayHit_DuringJump_CancelsJumpAndLandsOnFloor()
        {
            PlayerController player = PlayerController.Create(root.transform);
            float duration = GameConfig.ActionMaxSec;
            player.DoAction(ActionType.Jump, duration);
            player.Advance(duration / 4f);
            Assert.Greater(player.transform.position.y, GameConfig.FloorY);

            player.PlayHit();
            Assert.IsNull(player.CurrentAction);
            Assert.AreEqual(PlayerController.Reaction.Hit, player.CurrentReaction);
            Assert.AreEqual(GameConfig.FloorY, player.transform.position.y, Tolerance);

            player.SetX(5f);
            Assert.AreEqual(GameConfig.FloorY, player.transform.position.y, Tolerance, "취소된 점프의 높이가 남으면 안 됩니다");
        }

        [Test]
        public void PlayStumble_DuringWave_HidesSoundWaveEffect()
        {
            PlayerController player = PlayerController.Create(root.transform);
            SpriteRenderer wave = player.transform.Find("SoundWave").GetComponent<SpriteRenderer>();
            player.DoAction(ActionType.Wave, GameConfig.ActionMaxSec);
            Assert.IsTrue(wave.gameObject.activeSelf);

            player.PlayStumble();
            Assert.IsFalse(wave.gameObject.activeSelf, "반응 중에는 음파 이펙트가 숨겨져야 합니다");
            Assert.IsNull(player.CurrentAction);
        }

        [Test]
        public void DoAction_DuringReaction_CancelsReaction()
        {
            PlayerController player = PlayerController.Create(root.transform);
            player.PlayHit();
            player.DoAction(ActionType.Slide, GameConfig.HitFlashSec * 2f); // 취소된 반응 시간보다 길게
            Assert.AreEqual(PlayerController.Reaction.None, player.CurrentReaction);
            Assert.AreEqual(ActionType.Slide, player.CurrentAction);

            // 취소된 반응의 남은 시간이 동작을 끊으면 안 된다.
            player.Advance(GameConfig.HitFlashSec);
            Assert.AreEqual(ActionType.Slide, player.CurrentAction);

            player.PlayStumble();
            player.DoAction(ActionType.Wave, GameConfig.ActionMaxSec);
            Assert.AreEqual(PlayerController.Reaction.None, player.CurrentReaction);
            Assert.AreEqual(ActionType.Wave, player.CurrentAction);
        }

        [Test]
        public void ResetPose_ClearsReaction()
        {
            PlayerController player = PlayerController.Create(root.transform);
            SpriteRenderer body = player.GetComponent<SpriteRenderer>();
            Sprite idle = body.sprite;

            player.PlayStumble();
            player.ResetPose();
            Assert.AreEqual(PlayerController.Reaction.None, player.CurrentReaction);
            Assert.AreEqual(idle, body.sprite);
            Assert.AreEqual(GameConfig.FloorY, player.transform.position.y, Tolerance);
        }
    }
}
