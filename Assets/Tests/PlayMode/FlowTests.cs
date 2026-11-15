using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace NoiseRun.Tests
{
    /// <summary>
    /// Runs without InputTestFixture so the real AutoStart result is inspected.
    /// Other fixtures always leave a fresh Title-state Bootstrap behind, so the assertions
    /// hold regardless of test order.
    /// </summary>
    [PrebuildSetup(typeof(DevFileGuard)), PostBuildCleanup(typeof(DevFileGuard))]
    public class BootstrapAutoStartTests
    {
        [UnityTest]
        public IEnumerator AutoStart_CreatesSingleBootstrapInTitle()
        {
            yield return null;

            Assert.IsNotNull(Bootstrap.Instance);
            Assert.IsNotNull(Bootstrap.Instance.Flow);
            Assert.AreEqual(1, Object.FindObjectsByType<Bootstrap>().Length);
            Assert.AreEqual(GameState.Title, Bootstrap.Instance.Flow.State);
        }
    }

    [PrebuildSetup(typeof(DevFileGuard)), PostBuildCleanup(typeof(DevFileGuard))]
    public class FlowTests : InputTestFixture
    {
        const int MaxWaitFrames = 10;

        // NUnit runs base [SetUp] (InputTestFixture.Setup) before this one,
        // and this [TearDown] before the base TearDown.
        [SetUp]
        public void BootstrapSetUp()
        {
            Bootstrap.Restart(new FakeSongClock());
        }

        [TearDown]
        public void BootstrapTearDown()
        {
            // Leave a fresh, default Title-state Bootstrap for whatever runs next.
            Bootstrap.Restart(null);
        }

        [UnityTest]
        public IEnumerator Title_Enter_GoesToSelectFileOnce()
        {
            var flow = Bootstrap.Instance.Flow;
            Assert.AreEqual(GameState.Title, flow.State);

            var changes = new List<(GameState prev, GameState next)>();
            flow.StateChanged += (prev, next) => changes.Add((prev, next));

            var keyboard = InputSystem.AddDevice<Keyboard>();
            yield return null;

            Press(keyboard.enterKey);
            for (int i = 0; i < MaxWaitFrames && flow.State != GameState.SelectFile; i++)
                yield return null;

            Assert.AreEqual(GameState.SelectFile, flow.State);

            // Do not press Enter again: in SelectFile it opens the native dialog.
            Release(keyboard.enterKey);
            yield return null;
            yield return null;

            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual(GameState.Title, changes[0].prev);
            Assert.AreEqual(GameState.SelectFile, changes[0].next);
            Assert.AreEqual(GameState.SelectFile, flow.State);
        }

        [Test]
        public void Restart_LeavesSingleBootstrapInTitle()
        {
            var first = Bootstrap.Instance;
            var second = Bootstrap.Restart(new FakeSongClock());
            Bootstrap.Restart(new FakeSongClock());

            Assert.IsTrue(first == null, "previous Bootstrap should be destroyed immediately");
            Assert.IsTrue(second == null, "intermediate Bootstrap should be destroyed immediately");
            Assert.AreEqual(1, Object.FindObjectsByType<Bootstrap>().Length);
            Assert.IsNotNull(Bootstrap.Instance);
            Assert.AreEqual(GameState.Title, Bootstrap.Instance.Flow.State);
        }
    }
}
