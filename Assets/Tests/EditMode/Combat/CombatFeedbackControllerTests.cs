using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace TinyAdventure
{
    [TestFixture]
    public sealed class CombatFeedbackControllerTests
    {
        private sealed class CombatantRegistryStub : ICombatantRegistry
        {
            private readonly HashSet<CombatantMarker> combatants = new HashSet<CombatantMarker>();

            public bool Register(CombatantMarker combatant) => combatant != null && combatants.Add(combatant);
            public bool Unregister(CombatantMarker combatant) => combatant != null && combatants.Remove(combatant);
            public bool IsRegistered(CombatantMarker combatant) => combatant != null && combatants.Contains(combatant);
        }

        private sealed class GameplayStateStub : IGameplayStateProvider
        {
            public GameplayState CurrentState { get; set; } = GameplayState.Running;
        }

        private GameObject controllerGo;
        private CombatFeedbackController controller;
        private CombatFeedbackProfile profile;
        private RecordingDamageFeedbackSource damageSource;
        private GameplayStateStub stateProvider;
        private RecordingFeedbackModule moduleA;
        private RecordingFeedbackModule moduleB;
        private CombatantRegistryStub registry;

        private GameObject sourceGo;
        private GameObject targetGo;
        private CombatantMarker source;
        private CombatantMarker target;
        private HealthComponent targetHealth;

        [SetUp]
        public void SetUp()
        {
            controllerGo = new GameObject("CombatFeedbackController");
            controller = controllerGo.AddComponent<CombatFeedbackController>();

            profile = ScriptableObject.CreateInstance<CombatFeedbackProfile>();
            damageSource = new RecordingDamageFeedbackSource();
            stateProvider = new GameplayStateStub();
            moduleA = new RecordingFeedbackModule { Name = "ModuleA" };
            moduleB = new RecordingFeedbackModule { Name = "ModuleB" };

            registry = new CombatantRegistryStub();

            sourceGo = new GameObject("SourcePlayer");
            source = sourceGo.AddComponent<CombatantMarker>();
            source.ConfigureForTests(CombatantMarker.CombatantFaction.Player, "Knight");
            registry.Register(source);

            targetGo = new GameObject("TargetEnemy");
            target = targetGo.AddComponent<CombatantMarker>();
            target.ConfigureForTests(CombatantMarker.CombatantFaction.Enemy, "Enemy_Melee");
            targetHealth = targetGo.AddComponent<HealthComponent>();
            targetHealth.EnterDemo();
            registry.Register(target);

            controller.ConfigureForTests(
                damageSource,
                stateProvider,
                profile,
                new ICombatFeedbackModule[] { moduleA, moduleB });
        }

        [TearDown]
        public void TearDown()
        {
            if (profile != null) Object.DestroyImmediate(profile);
            if (controllerGo != null) Object.DestroyImmediate(controllerGo);
            if (sourceGo != null) Object.DestroyImmediate(sourceGo);
            if (targetGo != null) Object.DestroyImmediate(targetGo);
        }

        [Test]
        public void NormalHit_DispatchesToModules_WithNormalHitType()
        {
            CombatFeedbackRequest dispatched = default;
            bool eventFired = false;
            controller.FeedbackDispatched += req =>
            {
                dispatched = req;
                eventFired = true;
            };

            DamageRequest.TryCreate(
                registry, source, target, 10f, 1, AttackKinds.KnightSword, target.transform.position, 0d,
                out DamageRequest damage, out _);

            damageSource.Raise(target, damage);

            Assert.That(eventFired, Is.True, "FeedbackDispatched が発火する必要があります。");
            Assert.That(dispatched.HitType, Is.EqualTo(CombatHitType.Normal), "生存状態の被弾は Normal と判定される必要があります。");
            Assert.That(dispatched.Source, Is.SameAs(source));
            Assert.That(dispatched.Target, Is.SameAs(target));
            Assert.That(moduleA.PlayRequests.Count, Is.EqualTo(1), "ModuleA は 1 回の Play 呼び出しを受信する必要があります。");
            Assert.That(moduleB.PlayRequests.Count, Is.EqualTo(1), "ModuleB は 1 回の Play 呼び出しを受信する必要があります。");
            Assert.That(moduleA.PlayRequests[0].HitType, Is.EqualTo(CombatHitType.Normal));
        }

        [Test]
        public void LethalHit_DispatchesToModules_WithLethalHitType()
        {
            CombatFeedbackRequest dispatched = default;
            controller.FeedbackDispatched += req => dispatched = req;

            // 全HPを削り、対象を死亡またはHP0状態にする
            DamageRequest.TryCreate(
                registry, source, target, 100f, 1, AttackKinds.KnightSword, target.transform.position, 0d,
                out DamageRequest damage, out _);

            targetHealth.Receive(damage, out _);

            damageSource.Raise(target, damage);

            Assert.That(dispatched.HitType, Is.EqualTo(CombatHitType.Lethal), "HPゼロまたは死亡遷移時の被弾は Lethal と判定される必要があります。");
            Assert.That(moduleA.PlayRequests.Count, Is.EqualTo(1));
            Assert.That(moduleA.PlayRequests[0].HitType, Is.EqualTo(CombatHitType.Lethal));
        }

        [Test]
        public void SameSequenceAndTarget_DeduplicatesFeedback()
        {
            DamageRequest.TryCreate(
                registry, source, target, 10f, 1, AttackKinds.KnightSword, target.transform.position, 0d,
                out DamageRequest damage, out _);

            damageSource.Raise(target, damage);
            damageSource.Raise(target, damage);

            Assert.That(moduleA.PlayRequests.Count, Is.EqualTo(1), "同一攻撃シーケンスおよび同一対象への重複ヒットは除外される必要があります。");
        }

        [Test]
        public void DifferentSequence_DispatchesFeedbackTwice()
        {
            DamageRequest.TryCreate(
                registry, source, target, 10f, 1, AttackKinds.KnightSword, target.transform.position, 0d,
                out DamageRequest damage1, out _);

            DamageRequest.TryCreate(
                registry, source, target, 10f, 2, AttackKinds.KnightSword, target.transform.position, 0.1d,
                out DamageRequest damage2, out _);

            damageSource.Raise(target, damage1);
            damageSource.Raise(target, damage2);

            Assert.That(moduleA.PlayRequests.Count, Is.EqualTo(2), "異なる攻撃シーケンスのヒットは個別にディスパッチされる必要があります。");
        }

        [Test]
        public void SubmoduleException_IsIsolated_AndDoesNotBreakOtherModules()
        {
            moduleA.ThrowOnPlay = true;
            string lastDiag = string.Empty;
            controller.DiagnosticReported += diag => lastDiag = diag;

            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("ModuleA"));

            DamageRequest.TryCreate(
                registry, source, target, 10f, 1, AttackKinds.KnightSword, target.transform.position, 0d,
                out DamageRequest damage, out _);

            Assert.DoesNotThrow(() => damageSource.Raise(target, damage), "サブモジュールの例外が外部にスローされてはなりません。");
            Assert.That(moduleB.PlayRequests.Count, Is.EqualTo(1), "ModuleA が例外をスローしても ModuleB の実行を阻害してはなりません。");
            StringAssert.Contains("ModuleA", lastDiag, "ModuleA の例外を含む診断ログが記録される必要があります。");
        }

        [Test]
        public void TerminalState_BlocksSubsequentNewFeedback_UnlessAllowed()
        {
            DamageRequest.TryCreate(
                registry, source, target, 10f, 1, AttackKinds.KnightSword, target.transform.position, 0d,
                out DamageRequest damage1, out _);

            DamageRequest.TryCreate(
                registry, source, target, 10f, 2, AttackKinds.KnightSword, target.transform.position, 0.1d,
                out DamageRequest damage2, out _);

            stateProvider.CurrentState = GameplayState.Defeat;

            // 最初のイベントはディスパッチを許可（終局遷移の一撃）、以降は acceptNewFeedback = false
            damageSource.Raise(target, damage1);
            Assert.That(moduleA.PlayRequests.Count, Is.EqualTo(1));

            // 2回目のイベントは終局によりブロックされる
            damageSource.Raise(target, damage2);
            Assert.That(moduleA.PlayRequests.Count, Is.EqualTo(1), "終局後の新たなフィードバック要求はブロックされる必要があります。");
        }

        [Test]
        public void ClearRuntimeState_ResetsDeduplication()
        {
            DamageRequest.TryCreate(
                registry, source, target, 10f, 1, AttackKinds.KnightSword, target.transform.position, 0d,
                out DamageRequest damage, out _);

            damageSource.Raise(target, damage);
            Assert.That(moduleA.PlayRequests.Count, Is.EqualTo(1));

            int initialClearCount = moduleA.ClearCount;
            controller.ClearRuntimeState();
            Assert.That(moduleA.ClearCount, Is.EqualTo(initialClearCount + 1), "サブモジュールの ClearRuntimeState が呼び出される必要があります。");
            Assert.That(moduleA.PlayRequests.Count, Is.EqualTo(0), "サブモジュールの状態がクリアされる必要があります。");

            damageSource.Raise(target, damage);
            Assert.That(moduleA.PlayRequests.Count, Is.EqualTo(1), "ClearRuntimeState 後は重複除外キーがクリアされ、再トリガー可能である必要があります。");
        }

        [Test]
        public void InvalidRequest_HandledGracefullyWithoutExceptions()
        {
            string diag = string.Empty;
            controller.DiagnosticReported += msg => diag = msg;

            Assert.DoesNotThrow(() => damageSource.Raise(null, default), "null 対象および空リクエストで未処理例外がスローされてはなりません。");
            Assert.That(moduleA.PlayRequests.Count, Is.EqualTo(0));
            Assert.That(string.IsNullOrEmpty(diag), Is.False, "無効なリクエストに対する診断ログが報告される必要があります。");
        }
    }
}
