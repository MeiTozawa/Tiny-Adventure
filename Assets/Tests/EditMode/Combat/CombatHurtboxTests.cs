using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace TinyAdventure
{
    [TestFixture]
    public sealed class CombatHurtboxTests
    {
        private List<GameObject> disposables;

        [SetUp]
        public void SetUp()
        {
            disposables = new List<GameObject>();
        }

        [TearDown]
        public void TearDown()
        {
            if (disposables != null)
            {
                foreach (var obj in disposables)
                {
                    if (obj != null)
                    {
                        Object.DestroyImmediate(obj);
                    }
                }
                disposables.Clear();
            }
        }

        private GameObject CreateGameObject(string name)
        {
            var go = new GameObject(name);
            disposables.Add(go);
            return go;
        }

        [Test]
        public void CombatHurtbox_AutoResolvesReferences_FromParentAndSelf()
        {
            var root = CreateGameObject("CharacterRoot");
            var marker = root.AddComponent<CombatantMarker>();
            marker.SetIdentity(CombatantMarker.CombatantFaction.Player, "Knight");
            var health = root.AddComponent<HealthComponent>();

            var hurtboxGo = new GameObject("TorsoHurtbox");
            hurtboxGo.transform.SetParent(root.transform);
            var col = hurtboxGo.AddComponent<CapsuleCollider>();
            col.isTrigger = false; // ensure auto-enforced
            var hurtbox = hurtboxGo.AddComponent<CombatHurtbox>();

            Assert.That(hurtbox.Owner, Is.SameAs(marker), "CombatHurtbox should automatically find Owner from parent.");
            Assert.That(hurtbox.TargetHealth, Is.SameAs(health), "CombatHurtbox should automatically find HealthComponent from parent.");
            Assert.That(hurtbox.HurtboxCollider, Is.SameAs(col), "CombatHurtbox should automatically find Collider on same GameObject.");
            Assert.That(col.isTrigger, Is.True, "CombatHurtbox must enforce isTrigger = true on its collider.");
            Assert.That(hurtbox.Type, Is.EqualTo(HurtboxType.Torso));
            Assert.That(hurtbox.DamageMultiplier, Is.EqualTo(1.0f));
            Assert.That(hurtbox.IsActive, Is.True);
        }

        [Test]
        public void CombatHurtbox_IsActive_BecomesFalse_WhenTargetHealthDies()
        {
            var root = CreateGameObject("CharacterRoot");
            var marker = root.AddComponent<CombatantMarker>();
            marker.SetIdentity(CombatantMarker.CombatantFaction.Enemy, "Enemy_Melee");
            var health = root.AddComponent<HealthComponent>();
            health.EnterDemo();

            var hurtboxGo = new GameObject("TorsoHurtbox");
            hurtboxGo.transform.SetParent(root.transform);
            var col = hurtboxGo.AddComponent<CapsuleCollider>();
            var hurtbox = hurtboxGo.AddComponent<CombatHurtbox>();

            Assert.That(hurtbox.IsActive, Is.True);
            Assert.That(hurtbox.TargetHealth, Is.SameAs(health));

            bool diedFired = false;
            health.Died += () => diedFired = true;

            // Deal fatal damage using DamageRequest.TryCreate
            var attackerGo = CreateGameObject("Attacker");
            var attacker = attackerGo.AddComponent<CombatantMarker>();
            attacker.SetIdentity(CombatantMarker.CombatantFaction.Player, "Knight");

            var registry = new LocalCombatantRegistry();
            registry.Register(attacker);
            registry.Register(marker);

            var createResult = DamageRequest.Create(
                registry,
                attacker,
                marker,
                100f,
                1,
                AttackKinds.KnightSword,
                root.transform.position,
                1.0);
            Assert.That(createResult.IsOk, Is.True);
            var request = createResult.Value;

            Result receiveResult = health.Receive(request);
            Assert.That(receiveResult.IsOk, Is.True);
            Assert.That(health.IsAlive, Is.False, "HealthComponent should be dead after 100 damage.");
            Assert.That(diedFired, Is.True, "HealthComponent.Died event should have fired.");
            Assert.That(hurtbox.IsActive, Is.False, "Hurtbox must become inactive when target dies.");

            Assert.That(col.enabled, Is.False, "Hurtbox collider must be disabled upon death.");
        }

        [Test]
        public void CombatHitbox_RegistersCandidate_WhenStrikingCombatHurtbox()
        {
            // Setup Attacker
            var attackerGo = CreateGameObject("Attacker");
            var attackerMarker = attackerGo.AddComponent<CombatantMarker>();
            attackerMarker.SetIdentity(CombatantMarker.CombatantFaction.Player, "Knight");

            var weaponGo = new GameObject("Weapon");
            weaponGo.transform.SetParent(attackerGo.transform);
            var weaponCollider = weaponGo.AddComponent<BoxCollider>();
            weaponCollider.isTrigger = true;
            var hitbox = weaponGo.AddComponent<CombatHitbox>();

            var tracker = new AttackWindowTracker(attackerMarker, 5.0f);
            hitbox.SetWindowTracker(tracker);

            // Setup Target with CombatHurtbox
            var targetGo = CreateGameObject("Target");
            targetGo.transform.position = new Vector3(0, 0, 1.5f);
            var targetMarker = targetGo.AddComponent<CombatantMarker>();
            targetMarker.SetIdentity(CombatantMarker.CombatantFaction.Enemy, "Enemy_Melee");
            var targetHealth = targetGo.AddComponent<HealthComponent>();
            targetHealth.EnterDemo();

            var hurtboxGo = new GameObject("TorsoHurtbox");
            hurtboxGo.transform.SetParent(targetGo.transform);
            var targetCol = hurtboxGo.AddComponent<CapsuleCollider>();
            targetCol.isTrigger = true;
            var hurtbox = hurtboxGo.AddComponent<CombatHurtbox>();

            bool targetRegistered = false;
            tracker.TargetRegistered += (candidate, seq) =>
            {
                if (candidate == targetMarker && seq == 1)
                {
                    targetRegistered = true;
                }
            };

            // Open attack window
            Assert.That(tracker.BeginWindow(1, out _), Is.True);

            // Also call TryRegisterCandidate directly to test explicit method
            hitbox.TryRegisterCandidate(targetCol);

            Assert.That(targetRegistered, Is.True, "CombatHitbox should register candidate when colliding with ICombatHurtbox.");
        }

        [Test]
        public void CombatHitbox_IgnoresCollider_WhenNoCombatHurtboxPresent()
        {
            // Setup Attacker
            var attackerGo = CreateGameObject("Attacker");
            var attackerMarker = attackerGo.AddComponent<CombatantMarker>();
            attackerMarker.SetIdentity(CombatantMarker.CombatantFaction.Player, "Knight");

            var weaponGo = new GameObject("Weapon");
            weaponGo.transform.SetParent(attackerGo.transform);
            var weaponCollider = weaponGo.AddComponent<BoxCollider>();
            weaponCollider.isTrigger = true;
            var hitbox = weaponGo.AddComponent<CombatHitbox>();

            var tracker = new AttackWindowTracker(attackerMarker, 5.0f);
            hitbox.SetWindowTracker(tracker);

            // Setup Target with ordinary collider (e.g. movement CharacterController) without CombatHurtbox
            var targetGo = CreateGameObject("TargetWithoutHurtbox");
            targetGo.transform.position = new Vector3(0, 0, 1.5f);
            var targetMarker = targetGo.AddComponent<CombatantMarker>();
            targetMarker.SetIdentity(CombatantMarker.CombatantFaction.Enemy, "Enemy_Melee");
            var targetCol = targetGo.AddComponent<BoxCollider>(); // No CombatHurtbox

            bool targetRegistered = false;
            tracker.TargetRegistered += (candidate, seq) =>
            {
                targetRegistered = true;
            };

            // Open attack window
            Assert.That(tracker.BeginWindow(1, out _), Is.True);

            // Trigger hit candidate via raw collider directly
            hitbox.TryRegisterCandidate(targetCol);

            Assert.That(targetRegistered, Is.False, "CombatHitbox must reject colliders that do not have ICombatHurtbox.");
        }

        [Test]
        public void CombatHurtbox_DamageMultiplier_DefaultsToOneAndCanBeCustomized()
        {
            var go = CreateGameObject("HurtboxObject");
            var col = go.AddComponent<SphereCollider>();
            var hurtbox = go.AddComponent<CombatHurtbox>();

            Assert.That(hurtbox.DamageMultiplier, Is.EqualTo(1.0f));

            hurtbox.Initialize(null, null, col, HurtboxType.Head, 2.5f);
            Assert.That(hurtbox.DamageMultiplier, Is.EqualTo(2.5f));
            Assert.That(hurtbox.Type, Is.EqualTo(HurtboxType.Head));
        }
    }
}
