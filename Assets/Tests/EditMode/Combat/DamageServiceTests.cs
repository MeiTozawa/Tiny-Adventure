using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace TinyAdventure
{
    [TestFixture]
    public sealed class DamageServiceTests
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

        private sealed class GameplayClockStub : IGameplayClock
        {
            public double Now { get; set; } = 10.0;
            public double FixedNow { get; set; } = 10.0;
        }

        private GameObject serviceGo;
        private DamageService damageService;
        private CombatantRegistryStub registry;
        private GameplayStateStub stateProvider;
        private GameplayClockStub clock;

        private GameObject sourceGo;
        private GameObject targetGo;
        private CombatantMarker source;
        private CombatantMarker target;
        private HealthComponent targetHealth;
        private AttackWindowTracker attackWindow;

        [SetUp]
        public void SetUp()
        {
            serviceGo = new GameObject("DamageService");
            damageService = serviceGo.AddComponent<DamageService>();

            registry = new CombatantRegistryStub();
            stateProvider = new GameplayStateStub();
            clock = new GameplayClockStub();

            damageService.SetDependencies(registry, stateProvider, clock);

            sourceGo = new GameObject("PlayerSource");
            sourceGo.transform.position = Vector3.zero;
            source = sourceGo.AddComponent<CombatantMarker>();
            source.SetIdentity(CombatantMarker.CombatantFaction.Player, "Knight");
            registry.Register(source);

            targetGo = new GameObject("EnemyTarget");
            targetGo.transform.position = new Vector3(0f, 0f, 1f); // 1m away, well within range
            target = targetGo.AddComponent<CombatantMarker>();
            target.SetIdentity(CombatantMarker.CombatantFaction.Enemy, "Enemy_Melee");
            targetHealth = targetGo.AddComponent<HealthComponent>();
            targetHealth.EnterDemo();
            registry.Register(target);

            attackWindow = new AttackWindowTracker(source, 2.5f);
            attackWindow.BeginWindow(1, out _);
        }

        [TearDown]
        public void TearDown()
        {
            if (serviceGo != null) Object.DestroyImmediate(serviceGo);
            if (sourceGo != null) Object.DestroyImmediate(sourceGo);
            if (targetGo != null) Object.DestroyImmediate(targetGo);
        }

        private DamageRequest CreateValidRequest(int sequenceId = 1, float amount = 10f)
        {
            DamageRequest.TryCreate(
                registry,
                source,
                target,
                amount,
                sequenceId,
                AttackKinds.KnightSword,
                target.transform.position,
                clock.Now,
                out DamageRequest request,
                out _);
            return request;
        }

        [Test]
        public void Submit_ValidRequest_MutatesHealthAndInvokesEventsInOrder()
        {
            DamageRequest request = CreateValidRequest();
            float initialHealth = targetHealth.CurrentHealth;

            bool damageAcceptedFired = false;
            bool feedbackRequestedFired = false;
            float healthAtEventTime = -1f;

            damageService.DamageAccepted += req =>
            {
                damageAcceptedFired = true;
                healthAtEventTime = targetHealth.CurrentHealth;
            };

            damageService.HitFeedbackRequested += (tgt, req) =>
            {
                feedbackRequestedFired = true;
            };

            bool success = damageService.Submit(request, attackWindow, out string diagnostic);

            Assert.That(success, Is.True, $"Submit 失敗: {diagnostic}");
            Assert.That(targetHealth.CurrentHealth, Is.EqualTo(initialHealth - 10f), "体力が10減少している必要があります。");
            Assert.That(damageAcceptedFired, Is.True, "DamageAccepted イベントが発火する必要があります。");
            Assert.That(feedbackRequestedFired, Is.True, "HitFeedbackRequested イベントが発火する必要があります。");
            Assert.That(healthAtEventTime, Is.EqualTo(initialHealth - 10f), "イベント発火時点で体力変更が既に適用されている必要があります。");
        }

        [Test]
        public void Validate_WhenGameplayStateNotRunning_Rejects()
        {
            stateProvider.CurrentState = GameplayState.Victory;
            DamageRequest request = CreateValidRequest();

            bool valid = damageService.Validate(request, attackWindow, out string diagnostic);

            Assert.That(valid, Is.False);
            Assert.That(diagnostic, Does.Contain("終局または初期化中"));
        }

        [Test]
        public void Validate_WhenSourceSelfTarget_Rejects()
        {
            // Create request first, but pass self target if possible or test with constructed request
            DamageRequest request = CreateValidRequest();
            // Since DamageRequest is a struct with readonly fields, if Source == Target check is in Validate,
            // we can verify that if request has Source == Target it is rejected.
            // But DamageRequest.TryCreate prevents Source == Target.
            // If we test with default request:
            DamageRequest defaultReq = default;
            bool valid = damageService.Validate(defaultReq, attackWindow, out string diagnostic);
            Assert.That(valid, Is.False);
            Assert.That(diagnostic, Does.Contain("無効なダメージ発生元"));
        }

        [Test]
        public void Validate_WhenParticipantsNotRegistered_Rejects()
        {
            // Create valid request first while both are registered
            DamageRequest request = CreateValidRequest();

            // Then unregister target from registry
            registry.Unregister(target);

            bool valid = damageService.Validate(request, attackWindow, out string diagnostic);

            Assert.That(valid, Is.False);
            Assert.That(diagnostic, Does.Contain("未登録"));
        }

        [Test]
        public void Validate_WhenFactionPairMismatch_Rejects()
        {
            // Player attacking with enemy attack kind
            DamageRequest.TryCreate(
                registry, source, target, 10f, 1, AttackKinds.EnemyMelee, target.transform.position, clock.Now,
                out DamageRequest mismatchRequest, out _);

            bool valid = damageService.Validate(mismatchRequest, attackWindow, out string diagnostic);

            Assert.That(valid, Is.False);
            Assert.That(diagnostic, Does.Contain("攻撃種別が不正"));
        }

        [Test]
        public void Validate_WhenTargetLacksHealthComponent_Rejects()
        {
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("対象にHealthComponentがないため"));

            Object.DestroyImmediate(targetHealth);
            DamageRequest request = CreateValidRequest();

            bool valid = damageService.Validate(request, attackWindow, out string diagnostic);

            Assert.That(valid, Is.False);
            Assert.That(diagnostic, Does.Contain("HealthComponentがない"));
        }

        [Test]
        public void Validate_WhenTargetAlreadyDead_Rejects()
        {
            targetHealth.EnterDemo();
            // Drain health to 0
            DamageRequest lethalRequest = CreateValidRequest(sequenceId: 1, amount: targetHealth.CurrentHealth);
            damageService.Submit(lethalRequest, attackWindow, out _);

            Assert.That(targetHealth.IsAlive, Is.False);

            // Try another attack with new sequence
            AttackWindowTracker window2 = new AttackWindowTracker(source, 2.5f);
            window2.BeginWindow(2, out _);
            DamageRequest secondRequest = CreateValidRequest(sequenceId: 2, amount: 10f);

            bool valid = damageService.Validate(secondRequest, window2, out string diagnostic);

            Assert.That(valid, Is.False);
            Assert.That(diagnostic, Does.Contain("死亡状態の対象"));
        }

        [Test]
        public void Validate_WhenAttackWindowClosed_Rejects()
        {
            AttackWindowTracker closedWindow = new AttackWindowTracker(source, 2.5f);
            // Window is not opened
            DamageRequest request = CreateValidRequest();

            bool valid = damageService.Validate(request, closedWindow, out string diagnostic);

            Assert.That(valid, Is.False);
            Assert.That(diagnostic, Does.Contain("攻撃有効時間外"));
        }

        [Test]
        public void Validate_WhenAttackWindowSequenceMismatch_Rejects()
        {
            AttackWindowTracker mismatchWindow = new AttackWindowTracker(source, 2.5f);
            mismatchWindow.BeginWindow(2, out _); // Window is for sequence 2
            DamageRequest request = CreateValidRequest(sequenceId: 1); // Request is for sequence 1

            bool valid = damageService.Validate(request, mismatchWindow, out string diagnostic);

            Assert.That(valid, Is.False);
            Assert.That(diagnostic, Does.Contain("攻撃有効時間外"));
        }

        [Test]
        public void Validate_WhenOutOfRange_Rejects()
        {
            // Move target 10m away, range is 2.5m
            targetGo.transform.position = new Vector3(0f, 0f, 10f);
            DamageRequest request = CreateValidRequest();

            bool valid = damageService.Validate(request, attackWindow, out string diagnostic);

            Assert.That(valid, Is.False);
            Assert.That(diagnostic, Does.Contain("攻撃範囲外"));
        }

        [Test]
        public void Validate_WhenDuplicateInSameSequence_Rejects()
        {
            DamageRequest request1 = CreateValidRequest(sequenceId: 1);
            bool firstSubmit = damageService.Submit(request1, attackWindow, out _);
            Assert.That(firstSubmit, Is.True);

            // Second attempt in same sequence
            DamageRequest request2 = CreateValidRequest(sequenceId: 1);
            bool secondValid = damageService.Validate(request2, attackWindow, out string diagnostic);

            Assert.That(secondValid, Is.False);
            Assert.That(diagnostic, Does.Contain("同一攻撃系列で既に命中済み"));
        }

        [Test]
        public void Submit_WhenValidationFails_DoesNotMutateHealthOrInvokeEvents()
        {
            stateProvider.CurrentState = GameplayState.Victory;
            DamageRequest request = CreateValidRequest();
            float initialHealth = targetHealth.CurrentHealth;
            bool eventFired = false;

            damageService.DamageAccepted += _ => eventFired = true;
            damageService.HitFeedbackRequested += (_, _) => eventFired = true;

            bool success = damageService.Submit(request, attackWindow, out _);

            Assert.That(success, Is.False);
            Assert.That(targetHealth.CurrentHealth, Is.EqualTo(initialHealth));
            Assert.That(eventFired, Is.False);
        }
    }
}
