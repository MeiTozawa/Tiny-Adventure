using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TinyAdventure.Tests
{
    /// <summary>
    /// 実際のSampleSceneでGameFlowの初期化順序、SpawnSnapshot、終局時Clock停止を検証します。
    /// </summary>
    public sealed class SampleSceneGameFlowTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            SceneManager.LoadScene("SampleScene", LoadSceneMode.Single);
            yield return null;
            yield return null;
            LogAssert.ignoreFailingMessages = false;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            yield return null;
        }

        [UnityTest]
        public IEnumerator SampleSceneInitializesThroughRunningAndCapturesSpawnSnapshot()
        {
            GameFlowController flow = Object.FindAnyObjectByType<GameFlowController>();
            SceneReferenceRegistry registry = Object.FindAnyObjectByType<SceneReferenceRegistry>();
            Assert.That(flow, Is.Not.Null, "SampleSceneにGameFlowControllerがありません。");
            Assert.That(registry, Is.Not.Null, "SampleSceneにSceneReferenceRegistryがありません。");
            Assert.That(flow.CurrentState, Is.EqualTo(GameplayState.Running), flow.LastDiagnostic);
            Assert.That(registry.IsSnapshotCaptured, Is.True, "SpawnSnapshotが初期化時に保存されていません。");
            Assert.That(registry.ActiveEnemyCount, Is.EqualTo(3), "初期敵集合が3体として登録されていません。");
            CollectionAssert.AreEqual(
                new[]
                {
                    GameFlowInitializationStage.Boot,
                    GameFlowInitializationStage.Validation,
                    GameFlowInitializationStage.Registration,
                    GameFlowInitializationStage.SpawnSnapshot,
                    GameFlowInitializationStage.HealthAndEnemyInitialization,
                    GameFlowInitializationStage.HudPreparation,
                    GameFlowInitializationStage.Running
                },
                flow.InitializationTrace);
            yield return null;
        }

        [UnityTest]
        public IEnumerator GameplayClockStopsInTerminalStateAndResumesAfterReload()
        {
            GameFlowController flow = Object.FindAnyObjectByType<GameFlowController>();
            GameplayClock clock = Object.FindAnyObjectByType<GameplayClock>();
            Assert.That(flow, Is.Not.Null, "SampleSceneにGameFlowControllerがありません。");
            Assert.That(clock, Is.Not.Null, "SampleSceneにGameplayClockがありません。");
            yield return new WaitForFixedUpdate();
            int runningTickCount = clock.FixedTickCount;
            Assert.That(runningTickCount, Is.GreaterThan(0), "Running中のGameplayClock固定tickが発生していません。");

            Assert.That(flow.TrySetState(GameplayState.Victory), Is.True, "Victory状態へ遷移できません。");
            int terminalTickCount = clock.FixedTickCount;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.That(clock.FixedTickCount, Is.EqualTo(terminalTickCount), "終局状態でGameplayClock固定tickが継続しています。");

            LogAssert.ignoreFailingMessages = true;
            SceneManager.LoadScene("SampleScene", LoadSceneMode.Single);
            yield return null;
            yield return null;
            LogAssert.ignoreFailingMessages = false;
            yield return new WaitForFixedUpdate();
            GameFlowController reloadedFlow = Object.FindAnyObjectByType<GameFlowController>();
            GameplayClock reloadedClock = Object.FindAnyObjectByType<GameplayClock>();
            Assert.That(reloadedFlow.CurrentState, Is.EqualTo(GameplayState.Running), "再読み込み後にRunningへ復帰していません。");
            Assert.That(reloadedClock.IsPaused, Is.False, "再読み込み後のGameplayClockが停止しています。");
            Assert.That(reloadedClock.FixedTickCount, Is.GreaterThan(0), "再読み込み後にGameplayClock固定tickが再開していません。");
        }
    }
}
