using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using TinyAdventure;

namespace TinyAdventure.Tests
{
    /// <summary>
    /// 戦闘基礎契約を編集モードで検証します。
    /// </summary>
    public sealed class FoundationalContractsTests
    {
        private sealed class CombatantRegistryStub : ICombatantRegistry
        {
            private readonly HashSet<CombatantMarker> combatants = new();

            public void Register(CombatantMarker combatant)
            {
                if (combatant != null) combatants.Add(combatant);
            }

            public void Unregister(CombatantMarker combatant)
            {
                if (combatant != null) combatants.Remove(combatant);
            }

            public bool IsRegistered(CombatantMarker combatant)
            {
                return combatant != null && combatants.Contains(combatant);
            }
        }

        private GameObject sourceObject;
        private GameObject targetObject;
        private CombatantMarker source;
        private CombatantMarker target;
        private CombatantRegistryStub registry;

        [SetUp]
        public void SetUp()
        {
            sourceObject = new GameObject("攻撃者");
            targetObject = new GameObject("対象");
            source = sourceObject.AddComponent<CombatantMarker>();
            target = targetObject.AddComponent<CombatantMarker>();
            registry = new CombatantRegistryStub();
            registry.Register(source);
            registry.Register(target);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(sourceObject);
            Object.DestroyImmediate(targetObject);
        }

        [Test]
        public void RegisteredParticipantsAndPositiveFiniteDamageCreateRequest()
        {
            bool created = DamageRequest.TryCreate(
                registry,
                source,
                target,
                12.5f,
                1,
                AttackKinds.KnightSword,
                Vector3.zero,
                0d,
                out DamageRequest damageRequest,
                out string diagnostic);

            Assert.That(created, Is.True, diagnostic);
            Assert.That(damageRequest.IsStructurallyValid, Is.True);
            Assert.That(damageRequest.HasRegisteredParticipants(registry), Is.True);
            Assert.That(damageRequest.AttackSequenceId, Is.EqualTo(1));
        }

        [Test]
        public void UnregisteredParticipantOrInvalidDamageDoesNotCreateRequest()
        {
            registry.Unregister(target);
            bool unregisteredCreated = DamageRequest.TryCreate(
                registry,
                source,
                target,
                1f,
                1,
                AttackKinds.KnightSword,
                Vector3.zero,
                1d,
                out _,
                out string unregisteredDiagnostic);

            Assert.That(unregisteredCreated, Is.False);
            StringAssert.Contains("未登録", unregisteredDiagnostic);

            registry.Register(target);
            bool invalidAmountCreated = DamageRequest.TryCreate(
                registry,
                source,
                target,
                float.NaN,
                1,
                AttackKinds.KnightSword,
                Vector3.zero,
                1d,
                out _,
                out string invalidAmountDiagnostic);

            Assert.That(invalidAmountCreated, Is.False);
            StringAssert.Contains("無効なダメージ量", invalidAmountDiagnostic);
        }

        [Test]
        public void DeterministicDamageAndTimestampValuesPreserveRequestContract()
        {
            for (int index = 1; index <= 100; index++)
            {
                float amount = index / 10f;
                double timestamp = index * 0.125d;

                bool created = DamageRequest.TryCreate(
                    registry,
                    source,
                    target,
                    amount,
                    index,
                    AttackKinds.EnemyMelee,
                    new Vector3(index, 0f, -index),
                    timestamp,
                    out DamageRequest damageRequest,
                    out string diagnostic);

                Assert.That(created, Is.True, $"系列 {index}: {diagnostic}");
                Assert.That(damageRequest.Amount, Is.EqualTo(amount));
                Assert.That(damageRequest.Timestamp, Is.EqualTo(timestamp));
                Assert.That(damageRequest.HasRegisteredParticipants(registry), Is.True);
            }
        }

        [Test]
        public void SpawnSnapshotAcceptsPositiveFiniteInitialHealth()
        {
            Result<SpawnSnapshot> valid = SpawnSnapshot.Create(Vector3.one, Quaternion.identity, 100f);
            Assert.That(valid.IsOk, Is.True);
            Assert.That(valid.Value.IsValid, Is.True);

            Result<SpawnSnapshot> invalid = SpawnSnapshot.Create(Vector3.one, Quaternion.identity, 0f);
            Assert.That(invalid.IsErr, Is.True);
            Assert.That(invalid.Error, Is.EqualTo(GameError.InvalidParameter));
        }
    }
}
