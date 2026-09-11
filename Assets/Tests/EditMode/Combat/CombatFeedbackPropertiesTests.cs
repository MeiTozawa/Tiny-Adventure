using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace TinyAdventure
{
    [TestFixture]
    public sealed class CombatFeedbackPropertiesTests
    {
        private GameObject controllerGo;
        private CombatFeedbackController controller;
        private RecordingDamageFeedbackSource damageSource;
        private StubGameplayStateProvider stateProvider;
        private CombatFeedbackProfile profile;
        private StubCombatantRegistry registry;

        private RecordingAnimationFeedback animModule;
        private RecordingFeedbackModule vfxModule;
        private RecordingFeedbackModule audioModule;
        private RecordingFeedbackModule hitStopModule;
        private RecordingFeedbackModule cameraModule;

        private GameObject playerGo;
        private GameObject enemyGo;
        private CombatantMarker playerMarker;
        private CombatantMarker enemyMarker;
        private HealthComponent enemyHealth;

        [SetUp]
        public void SetUp()
        {
            controllerGo = new GameObject("CombatFeedbackController");
            controller = controllerGo.AddComponent<CombatFeedbackController>();

            damageSource = new RecordingDamageFeedbackSource();
            stateProvider = new StubGameplayStateProvider();
            profile = ScriptableObject.CreateInstance<CombatFeedbackProfile>();
            registry = new StubCombatantRegistry();

            animModule = new RecordingAnimationFeedback();
            vfxModule = new RecordingFeedbackModule { Name = "Vfx" };
            audioModule = new RecordingFeedbackModule { Name = "Audio" };
            hitStopModule = new RecordingFeedbackModule { Name = "HitStop" };
            cameraModule = new RecordingFeedbackModule { Name = "Camera" };

            controller.ConfigureForTests(
                damageSource,
                stateProvider,
                profile,
                new ICombatFeedbackModule[] { animModule, vfxModule, audioModule, hitStopModule, cameraModule });

            playerGo = new GameObject("Player");
            playerMarker = playerGo.AddComponent<CombatantMarker>();
            playerMarker.ConfigureForTests(CombatantMarker.CombatantFaction.Player, "Knight");

            enemyGo = new GameObject("Enemy");
            enemyMarker = enemyGo.AddComponent<CombatantMarker>();
            enemyMarker.ConfigureForTests(CombatantMarker.CombatantFaction.Enemy, "Enemy_Melee");
            enemyHealth = enemyGo.AddComponent<HealthComponent>();
            enemyHealth.EnterDemo();
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
        public void Property1_FeedbackClassificationFidelity_NormalVsLethal()
        {
            // 1. 普通攻击命中（目标存活且血量大于0）
            var normalDmg = TestDamageRequestFactory.Create(registry, playerMarker, enemyMarker, 10f, 1);
            damageSource.Raise(enemyMarker, normalDmg);

            Assert.That(vfxModule.PlayRequests.Count, Is.EqualTo(1));
            Assert.That(vfxModule.PlayRequests[0].HitType, Is.EqualTo(CombatHitType.Normal));
            Assert.That(audioModule.PlayRequests.Count, Is.EqualTo(1));
            Assert.That(audioModule.PlayRequests[0].HitType, Is.EqualTo(CombatHitType.Normal));
            Assert.That(hitStopModule.PlayRequests.Count, Is.EqualTo(1));
            Assert.That(hitStopModule.PlayRequests[0].HitType, Is.EqualTo(CombatHitType.Normal));
            Assert.That(cameraModule.PlayRequests.Count, Is.EqualTo(1));
            Assert.That(cameraModule.PlayRequests[0].HitType, Is.EqualTo(CombatHitType.Normal));
            Assert.That(animModule.NormalHitRequests.Count, Is.EqualTo(1));
            Assert.That(animModule.LethalHitRequests.Count, Is.EqualTo(0));

            // 2. 致死攻击命中（扣减全部生命值）
            var lethalDmg = TestDamageRequestFactory.Create(registry, playerMarker, enemyMarker, 1000f, 2);
            enemyHealth.Receive(lethalDmg, out _);
            damageSource.Raise(enemyMarker, lethalDmg);

            Assert.That(vfxModule.PlayRequests.Count, Is.EqualTo(2));
            Assert.That(vfxModule.PlayRequests[1].HitType, Is.EqualTo(CombatHitType.Lethal));
            Assert.That(audioModule.PlayRequests.Count, Is.EqualTo(2));
            Assert.That(audioModule.PlayRequests[1].HitType, Is.EqualTo(CombatHitType.Lethal));
            Assert.That(hitStopModule.PlayRequests.Count, Is.EqualTo(2));
            Assert.That(hitStopModule.PlayRequests[1].HitType, Is.EqualTo(CombatHitType.Lethal));
            Assert.That(cameraModule.PlayRequests.Count, Is.EqualTo(2));
            Assert.That(cameraModule.PlayRequests[1].HitType, Is.EqualTo(CombatHitType.Lethal));
            Assert.That(animModule.LethalHitRequests.Count, Is.EqualTo(1));
        }

        [Test]
        public void Property2_SameAttackSequence_DeduplicatesHitFeedback()
        {
            // 同一攻击序列（sequenceId = 42）对同一目标连续触发 5 次
            var dmg = TestDamageRequestFactory.Create(registry, playerMarker, enemyMarker, 10f, 42);

            for (int i = 0; i < 5; i++)
            {
                damageSource.Raise(enemyMarker, dmg);
            }

            // 所有表现子模块必须且仅收到 1 次分发请求
            Assert.That(vfxModule.PlayRequests.Count, Is.EqualTo(1), "同一攻击序列对同一目标仅允许响应 1 次 VFX。");
            Assert.That(audioModule.PlayRequests.Count, Is.EqualTo(1), "同一攻击序列对同一目标仅允许响应 1 次 Audio。");
            Assert.That(hitStopModule.PlayRequests.Count, Is.EqualTo(1), "同一攻击序列对同一目标仅允许响应 1 次 HitStop。");
            Assert.That(cameraModule.PlayRequests.Count, Is.EqualTo(1), "同一攻击序列对同一目标仅允许响应 1 次 Camera。");
            Assert.That(animModule.NormalHitRequests.Count, Is.EqualTo(1), "同一攻击序列对同一目标仅允许响应 1 次动画受击。");
        }

        [Test]
        public void Property9_DeathAudioPlaysAtMostOnce_AndDecouplesFromHitLethalAudio()
        {
            // 验证 CombatDeathAudioRouter 的独立性与至多播放一次
            var deathRouterGo = new GameObject("DeathRouter");
            var deathRouter = deathRouterGo.AddComponent<CombatDeathAudioRouter>();

            var audioGo = new GameObject("CombatAudio");
            var audioController = audioGo.AddComponent<CombatAudioController>();
            var playbackAdapter = new RecordingAudioPlaybackAdapter();

            var deathClip = AudioClip.Create("SFX_Enemy_Die", 100, 1, 44100, false);
            var lethalHitClip = AudioClip.Create("SFX_Hit_Lethal", 100, 1, 44100, false);

            var lethalVariant = profile.LethalHit;
            lethalVariant.hitClip = lethalHitClip;

            profile.ConfigureForTests(
                profile.NormalHit,
                lethalVariant,
                enemyDeath: deathClip);

            audioController.ConfigureForTests(playbackAdapter, profile);

            var stubEnemyHealth = new StubHealthDeathSource
            {
                Marker = enemyMarker
            };

            deathRouter.ConfigureForTests(null, new[] { stubEnemyHealth }, audioController);

            // 1. 触发致死命中（SFX_Hit_Lethal 通过 HitFeedback 播放）
            var lethalDmg = TestDamageRequestFactory.Create(registry, playerMarker, enemyMarker, 100f, 1);
            var lethalRequest = new CombatFeedbackRequest(
                CombatHitType.Lethal,
                playerMarker,
                enemyMarker,
                lethalDmg,
                enemyMarker.transform.position,
                Vector3.forward,
                true,
                false,
                default);

            audioController.Play(lethalRequest);
            Assert.That(playbackAdapter.PlayRecords.Count, Is.EqualTo(1));
            Assert.That(playbackAdapter.PlayRecords[0].Clip, Is.SameAs(lethalHitClip), "致死命中应播放 SFX_Hit_Lethal。");

            // 2. 目标死亡事件触发（SFX_Enemy_Die 通过 DeathRouter 播放）
            stubEnemyHealth.TriggerDied();
            Assert.That(playbackAdapter.PlayRecords.Count, Is.EqualTo(2));
            Assert.That(playbackAdapter.PlayRecords[1].Clip, Is.SameAs(deathClip), "死亡事件应独立播放 SFX_Enemy_Die。");

            // 3. 再次触发死亡事件（去重，不重复播放死亡音）
            stubEnemyHealth.TriggerDied();
            Assert.That(playbackAdapter.PlayRecords.Count, Is.EqualTo(2), "重复死亡事件绝不再次播放死亡音效。");

            Object.DestroyImmediate(deathRouterGo);
            Object.DestroyImmediate(audioGo);
            Object.DestroyImmediate(deathClip);
            Object.DestroyImmediate(lethalHitClip);
        }
    }
}
