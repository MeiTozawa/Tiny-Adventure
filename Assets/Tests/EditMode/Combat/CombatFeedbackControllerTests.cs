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

            Assert.That(eventFired, Is.True, "FeedbackDispatched 应该被触发。");
            Assert.That(dispatched.HitType, Is.EqualTo(CombatHitType.Normal), "存活状态下的受击应该被判定为 Normal。");
            Assert.That(dispatched.Source, Is.SameAs(source));
            Assert.That(dispatched.Target, Is.SameAs(target));
            Assert.That(moduleA.PlayRequests.Count, Is.EqualTo(1), "ModuleA 应该接收到 1 次 Play 调度。");
            Assert.That(moduleB.PlayRequests.Count, Is.EqualTo(1), "ModuleB 应该接收到 1 次 Play 调度。");
            Assert.That(moduleA.PlayRequests[0].HitType, Is.EqualTo(CombatHitType.Normal));
        }

        [Test]
        public void LethalHit_DispatchesToModules_WithLethalHitType()
        {
            CombatFeedbackRequest dispatched = default;
            controller.FeedbackDispatched += req => dispatched = req;

            // 扣除全部生命值使目标进入死亡或生命值为0
            DamageRequest.TryCreate(
                registry, source, target, 100f, 1, AttackKinds.KnightSword, target.transform.position, 0d,
                out DamageRequest damage, out _);

            targetHealth.Receive(damage, out _);

            damageSource.Raise(target, damage);

            Assert.That(dispatched.HitType, Is.EqualTo(CombatHitType.Lethal), "生命值归零或死亡转换下的受击应该被判定为 Lethal。");
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

            Assert.That(moduleA.PlayRequests.Count, Is.EqualTo(1), "同一攻击系列与目标的重复命中应该被去重。");
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

            Assert.That(moduleA.PlayRequests.Count, Is.EqualTo(2), "不同攻击系列的命中应该分别分发。");
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

            Assert.DoesNotThrow(() => damageSource.Raise(target, damage), "子模块异常不应向外抛出。");
            Assert.That(moduleB.PlayRequests.Count, Is.EqualTo(1), "ModuleA 抛出异常时不应阻断 ModuleB 执行。");
            StringAssert.Contains("ModuleA", lastDiag, "应该记录包含 ModuleA 异常的中文诊断。");
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

            // 第一次事件允许分发（终局过渡那一击），但此后标记 acceptNewFeedback = false
            damageSource.Raise(target, damage1);
            Assert.That(moduleA.PlayRequests.Count, Is.EqualTo(1));

            // 第二次事件应该被终局阻断
            damageSource.Raise(target, damage2);
            Assert.That(moduleA.PlayRequests.Count, Is.EqualTo(1), "终局后后续新的反馈请求应该被阻断。");
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
            Assert.That(moduleA.ClearCount, Is.EqualTo(initialClearCount + 1), "子模块的 ClearRuntimeState 应该被调用。");
            Assert.That(moduleA.PlayRequests.Count, Is.EqualTo(0), "子模块状态应该被清空。");

            damageSource.Raise(target, damage);
            Assert.That(moduleA.PlayRequests.Count, Is.EqualTo(1), "ClearRuntimeState 后应该清除去重键，允许重新触发。");
        }

        [Test]
        public void InvalidRequest_HandledGracefullyWithoutExceptions()
        {
            string diag = string.Empty;
            controller.DiagnosticReported += msg => diag = msg;

            Assert.DoesNotThrow(() => damageSource.Raise(null, default), "空目标与空请求不应抛出未捕获异常。");
            Assert.That(moduleA.PlayRequests.Count, Is.EqualTo(0));
            Assert.That(string.IsNullOrEmpty(diag), Is.False, "应该报告无效请求的中文诊断。");
        }
    }
}
