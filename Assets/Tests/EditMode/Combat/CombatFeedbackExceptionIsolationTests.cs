using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace TinyAdventure
{
    [TestFixture]
    public sealed class CombatFeedbackExceptionIsolationTests
    {
        private GameObject controllerGo;
        private CombatFeedbackController controller;
        private RecordingDamageFeedbackSource damageSource;
        private StubGameplayStateProvider stateProvider;
        private CombatFeedbackProfile profile;
        private StubCombatantRegistry registry;

        private GameObject playerGo;
        private GameObject enemyGo;
        private CombatantMarker playerMarker;
        private CombatantMarker enemyMarker;

        [SetUp]
        public void SetUp()
        {
            controllerGo = new GameObject("CombatFeedbackController");
            controller = controllerGo.AddComponent<CombatFeedbackController>();

            damageSource = new RecordingDamageFeedbackSource();
            stateProvider = new StubGameplayStateProvider();
            profile = ScriptableObject.CreateInstance<CombatFeedbackProfile>();
            registry = new StubCombatantRegistry();

            playerGo = new GameObject("Player");
            playerMarker = playerGo.AddComponent<CombatantMarker>();
            playerMarker.ConfigureForTests(CombatantMarker.CombatantFaction.Player, "Knight");

            enemyGo = new GameObject("Enemy");
            enemyMarker = enemyGo.AddComponent<CombatantMarker>();
            enemyMarker.ConfigureForTests(CombatantMarker.CombatantFaction.Enemy, "Enemy_Melee");
            enemyGo.transform.position = new Vector3(0, 0, 2);

            registry.Register(playerMarker);
            registry.Register(enemyMarker);
        }

        [TearDown]
        public void TearDown()
        {
            if (controllerGo != null) Object.DestroyImmediate(controllerGo);
            if (playerGo != null) Object.DestroyImmediate(playerGo);
            if (enemyGo != null) Object.DestroyImmediate(enemyGo);
            if (profile != null) Object.DestroyImmediate(profile);
        }

        [Test]
        public void Property8_SingleModuleException_IsolatesAndAllowsDownstreamModulesToExecute()
        {
            // 创建 5 个模块，第 2 个模块（VFX）抛出异常
            var animModule = new RecordingFeedbackModule { Name = "Animation" };
            var faultingVfx = new RecordingFeedbackModule { Name = "Vfx", ThrowOnPlay = true };
            var audioModule = new RecordingFeedbackModule { Name = "Audio" };
            var hitStopModule = new RecordingFeedbackModule { Name = "HitStop" };
            var cameraModule = new RecordingFeedbackModule { Name = "Camera" };

            controller.ConfigureForTests(
                damageSource,
                stateProvider,
                profile,
                new ICombatFeedbackModule[] { animModule, faultingVfx, audioModule, hitStopModule, cameraModule });

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*子模块「Vfx」.*异常.*"));

            var dmg = TestDamageRequestFactory.Create(registry, playerMarker, enemyMarker, 10f, 1);
            Assert.DoesNotThrow(() => damageSource.Raise(enemyMarker, dmg));

            // 1. 前置模块正常执行
            Assert.That(animModule.PlayRequests.Count, Is.EqualTo(1));

            // 2. 异常模块尝试执行
            Assert.That(faultingVfx.PlayRequests.Count, Is.EqualTo(0));

            // 3. 后续模块仍然正常收到请求并执行！
            Assert.That(audioModule.PlayRequests.Count, Is.EqualTo(1));
            Assert.That(hitStopModule.PlayRequests.Count, Is.EqualTo(1));
            Assert.That(cameraModule.PlayRequests.Count, Is.EqualTo(1));
        }

        [Test]
        public void Property8_ClearRuntimeStateException_IsIsolated()
        {
            var normalModule = new RecordingFeedbackModule { Name = "Normal" };
            var throwingModule = new RecordingFeedbackModule { Name = "Throwing" };
            var faultingModule = new FaultingClearModule { ShouldThrow = false };

            controller.ConfigureForTests(
                damageSource,
                stateProvider,
                profile,
                new ICombatFeedbackModule[] { normalModule, faultingModule, throwingModule });

            int beforeNormal = normalModule.ClearCount;
            int beforeThrowing = throwingModule.ClearCount;

            faultingModule.ShouldThrow = true;
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*清理异常.*"));
            Assert.DoesNotThrow(() => controller.ClearRuntimeState());

            // normalModule 和 throwingModule 均收到清理调用
            Assert.That(normalModule.ClearCount, Is.EqualTo(beforeNormal + 1));
            Assert.That(throwingModule.ClearCount, Is.EqualTo(beforeThrowing + 1));

            faultingModule.ShouldThrow = false;
        }

        private sealed class FaultingClearModule : ICombatFeedbackModule
        {
            public bool ShouldThrow { get; set; }

            public void Play(CombatFeedbackRequest request) { }

            public void ClearRuntimeState()
            {
                if (ShouldThrow)
                {
                    throw new System.InvalidOperationException("模拟清理异常");
                }
            }
        }
    }
}
