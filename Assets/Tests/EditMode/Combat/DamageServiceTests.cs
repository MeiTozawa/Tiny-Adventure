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

            public void Register(CombatantMarker combatant)
            {
                if (combatant != null) combatants.Add(combatant);
            }
            public void Unregister(CombatantMarker combatant)
            {
                if (combatant != null) combatants.Remove(combatant);
            }
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

            damageService.Construct(registry, stateProvider, clock);

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
            attackWindow.BeginWindow(1);
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
            return DamageRequest.Create(
                registry,
                source,
                target,
                amount,
                sequenceId,
                AttackKinds.KnightSword,
                target.transform.position,
                clock.Now).Value;
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

            Result result = damageService.Submit(request, attackWindow);

            Assert.That(result.IsOk, Is.True);
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

            Result result = damageService.Validate(request, attackWindow);

            Assert.That(result.Error, Is.EqualTo(GameError.InvalidState));
        }

        [Test]
        public void Validate_WhenSourceSelfTarget_Rejects()
        {
            DamageRequest defaultReq = default;
            Result result = damageService.Validate(defaultReq, attackWindow);
            Assert.That(result.Error, Is.EqualTo(GameError.InvalidParameter));
        }

        [Test]
        public void Validate_WhenSourceIsNotAvailableForCombat_Rejects()
        {
            DamageRequest request = CreateValidRequest();
            sourceGo.SetActive(false);

            Result result = damageService.Validate(request, attackWindow);

            Assert.That(result.Error, Is.EqualTo(GameError.InvalidParameter));
        }

        [Test]
        public void Validate_WhenParticipantsNotRegistered_Rejects()
        {
            DamageRequest request = CreateValidRequest();

            registry.Unregister(target);

            Result result = damageService.Validate(request, attackWindow);

            Assert.That(result.Error, Is.EqualTo(GameError.CombatantNotRegistered));
        }

        [Test]
        public void Validate_WhenFactionPairMismatch_Rejects()
        {
            DamageRequest mismatchRequest = DamageRequest.Create(
                registry, source, target, 10f, 1, AttackKinds.EnemyMelee, target.transform.position, clock.Now).Value;

            Result result = damageService.Validate(mismatchRequest, attackWindow);

            Assert.That(result.Error, Is.EqualTo(GameError.InvalidFactionPair));
        }

        [Test]
        public void Validate_WhenTargetLacksHealthComponent_ThrowsAssertionException()
        {
            Object.DestroyImmediate(targetHealth);
            DamageRequest request = CreateValidRequest();

            Assert.Throws<UnityEngine.Assertions.AssertionException>(() => damageService.Validate(request, attackWindow));
        }

        [Test]
        public void Validate_WhenTargetAlreadyDead_Rejects()
        {
            targetHealth.EnterDemo();
            DamageRequest lethalRequest = CreateValidRequest(sequenceId: 1, amount: targetHealth.CurrentHealth);
            damageService.Submit(lethalRequest, attackWindow);

            Assert.That(targetHealth.IsAlive, Is.False);

            AttackWindowTracker window2 = new AttackWindowTracker(source, 2.5f);
            window2.BeginWindow(2);
            DamageRequest secondRequest = CreateValidRequest(sequenceId: 2, amount: 10f);

            Result result = damageService.Validate(secondRequest, window2);

            Assert.That(result.Error, Is.EqualTo(GameError.TargetDead));
        }

        [Test]
        public void Validate_WhenAttackWindowClosed_Rejects()
        {
            AttackWindowTracker closedWindow = new AttackWindowTracker(source, 2.5f);
            DamageRequest request = CreateValidRequest();

            Result result = damageService.Validate(request, closedWindow);

            Assert.That(result.Error, Is.EqualTo(GameError.AttackWindowClosed));
        }

        [Test]
        public void Validate_WhenAttackWindowSequenceMismatch_Rejects()
        {
            AttackWindowTracker mismatchWindow = new AttackWindowTracker(source, 2.5f);
            mismatchWindow.BeginWindow(2);
            DamageRequest request = CreateValidRequest(sequenceId: 1);

            Result result = damageService.Validate(request, mismatchWindow);

            Assert.That(result.Error, Is.EqualTo(GameError.AttackWindowClosed));
        }

        [Test]
        public void Validate_WhenOutOfRange_Rejects()
        {
            targetGo.transform.position = new Vector3(0f, 0f, 10f);
            DamageRequest request = CreateValidRequest();

            Result result = damageService.Validate(request, attackWindow);

            Assert.That(result.Error, Is.EqualTo(GameError.OutOfRange));
        }

        [Test]
        public void Validate_WhenDuplicateInSameSequence_Rejects()
        {
            DamageRequest request1 = CreateValidRequest(sequenceId: 1);
            Result firstSubmit = damageService.Submit(request1, attackWindow);
            Assert.That(firstSubmit.IsOk, Is.True);

            DamageRequest request2 = CreateValidRequest(sequenceId: 1);
            Result secondValid = damageService.Validate(request2, attackWindow);

            Assert.That(secondValid.Error, Is.EqualTo(GameError.DuplicateHitInSequence));
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

            Result result = damageService.Submit(request, attackWindow);

            Assert.That(result.IsErr, Is.True);
            Assert.That(targetHealth.CurrentHealth, Is.EqualTo(initialHealth));
            Assert.That(eventFired, Is.False);
        }
    }
}
