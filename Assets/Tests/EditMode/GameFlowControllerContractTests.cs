using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace TinyAdventure.Tests
{
    /// <summary>
    /// GameFlow、SceneReferenceRegistry、SpawnSnapshotの初期化契約を検証します。
    /// </summary>
    public sealed class GameFlowControllerContractTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int index = objects.Count - 1; index >= 0; index--)
            {
                if (objects[index] != null)
                {
                    Object.DestroyImmediate(objects[index]);
                }
            }

            objects.Clear();
        }

        [Test]
        public void NormalInitializationRunsInRequiredOrderAndEndsRunning()
        {
            GameObject playerObject = CreateCombatant("Knight", CombatantMarker.CombatantFaction.Player, "Knight", new Vector3(1f, 0f, 2f));
            GameObject enemyObject = CreateCombatant("Enemy_01", CombatantMarker.CombatantFaction.Enemy, "Enemy_01", new Vector3(4f, 0f, 2f));
            GameFlowController flow = CreateFlowRoot(out SceneReferenceRegistry registry, out GameplayClock clock);

            flow.Start();
            Assert.That(flow.CurrentState, Is.EqualTo(GameplayState.Running));
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
            Assert.That(clock.IsPaused, Is.False);
            Assert.That(registry.ActiveEnemyCount, Is.EqualTo(1));
            Assert.That(registry.SpawnSnapshots[playerObject.GetComponent<CombatantMarker>()].Position, Is.EqualTo(new Vector3(1f, 0f, 2f)));
            Assert.That(registry.SpawnSnapshots[enemyObject.GetComponent<CombatantMarker>()].Rotation, Is.EqualTo(Quaternion.identity));
        }

        [Test]
        public void EmptyEnemyCollectionEntersVictoryImmediately()
        {
            CreateCombatant("Knight", CombatantMarker.CombatantFaction.Player, "Knight", Vector3.zero);
            GameFlowController flow = CreateFlowRoot(out SceneReferenceRegistry registry, out GameplayClock clock);

            flow.Start();
            Assert.That(registry.ActiveEnemyCount, Is.EqualTo(0));
            Assert.That(flow.CurrentState, Is.EqualTo(GameplayState.Victory));
            Assert.That(clock.IsPaused, Is.True);
            CollectionAssert.Contains(flow.InitializationTrace, GameFlowInitializationStage.Victory);
        }

        [Test]
        public void PlayerStartingAtZeroHealthEntersDefeatWithoutHealthReset()
        {
            GameObject playerObject = CreateCombatant("Knight", CombatantMarker.CombatantFaction.Player, "Knight", Vector3.zero);
            HealthComponent health = playerObject.GetComponent<HealthComponent>();
            SetCurrentHealth(health, 0f);
            GameObject enemyObject = CreateCombatant("Enemy_01", CombatantMarker.CombatantFaction.Enemy, "Enemy_01", Vector3.forward * 3f);
            GameFlowController flow = CreateFlowRoot(out _, out _);

            flow.Start();
            Assert.That(flow.CurrentState, Is.EqualTo(GameplayState.Defeat));
            Assert.That(health.CurrentHealth, Is.EqualTo(0f));
            Assert.That(enemyObject.GetComponent<HealthComponent>().CurrentHealth, Is.EqualTo(100f));
        }

        [Test]
        public void TerminalStateRejectsLaterStateWrites()
        {
            CreateCombatant("Knight", CombatantMarker.CombatantFaction.Player, "Knight", Vector3.zero);
            CreateCombatant("Enemy_01", CombatantMarker.CombatantFaction.Enemy, "Enemy_01", Vector3.forward * 2f);
            GameFlowController flow = CreateFlowRoot(out _, out _);
            flow.Start();
            Assert.That(flow.SetState(GameplayState.Victory).IsOk, Is.True);
            Assert.That(flow.SetState(GameplayState.Defeat).Error, Is.EqualTo(GameError.StateAlreadyTerminal));
            Assert.That(flow.SetState(GameplayState.Running).Error, Is.EqualTo(GameError.StateAlreadyTerminal));
            Assert.That(flow.CurrentState, Is.EqualTo(GameplayState.Victory));
        }

        [Test]
        public void RegistryReportsNullAndDuplicateRegistrations()
        {
            GameObject registryObject = Track(new GameObject("SceneReferenceRegistryTest"));
            SceneReferenceRegistry registry = registryObject.AddComponent<SceneReferenceRegistry>();
            GameObject markerObject = Track(new GameObject("Knight"));
            CombatantMarker marker = markerObject.AddComponent<CombatantMarker>();
            SetPrivateField(marker, "combatantId", "Knight");

            Assert.Throws<UnityEngine.Assertions.AssertionException>(() => registry.Register(null));
            registry.Register(marker);
            Assert.That(registry.IsRegistered(marker), Is.True);
            registry.Register(marker);
            Assert.That(registry.IsRegistered(marker), Is.True);
        }

        [Test]
        public void SpawnSnapshotPreservesPositionRotationAndInitialHealth()
        {
            GameObject playerObject = CreateCombatant("Knight", CombatantMarker.CombatantFaction.Player, "Knight", new Vector3(2f, 0f, 3f));
            playerObject.transform.rotation = Quaternion.Euler(0f, 37f, 0f);
            GameObject enemyObject = CreateCombatant("Enemy_01", CombatantMarker.CombatantFaction.Enemy, "Enemy_01", new Vector3(-2f, 0f, 1f));
            enemyObject.transform.rotation = Quaternion.Euler(0f, -22f, 0f);
            GameFlowController flow = CreateFlowRoot(out SceneReferenceRegistry registry, out _);

            flow.Start();
            SpawnSnapshot playerSnapshot = registry.SpawnSnapshots[playerObject.GetComponent<CombatantMarker>()];
            SpawnSnapshot enemySnapshot = registry.SpawnSnapshots[enemyObject.GetComponent<CombatantMarker>()];
            Assert.That(playerSnapshot.Position, Is.EqualTo(new Vector3(2f, 0f, 3f)));
            Assert.That(playerSnapshot.Rotation, Is.EqualTo(playerObject.transform.rotation));
            Assert.That(playerSnapshot.InitialHealth, Is.EqualTo(100f));
            Assert.That(enemySnapshot.Position, Is.EqualTo(new Vector3(-2f, 0f, 1f)));
            Assert.That(enemySnapshot.Rotation, Is.EqualTo(enemyObject.transform.rotation));
            Assert.That(enemySnapshot.InitialHealth, Is.EqualTo(100f));
        }

        private GameFlowController CreateFlowRoot(out SceneReferenceRegistry registry, out GameplayClock clock)
        {
            GameObject root = Track(new GameObject("GameRoot"));
            root.AddComponent<GameFlowController>();
            root.AddComponent<DamageService>();
            clock = root.AddComponent<GameplayClock>();
            registry = root.AddComponent<SceneReferenceRegistry>();
            return root.GetComponent<GameFlowController>();
        }

        private GameObject CreateCombatant(string name, CombatantMarker.CombatantFaction faction, string combatantId, Vector3 position)
        {
            GameObject combatant = Track(new GameObject(name));
            combatant.transform.position = position;
            CombatantMarker marker = combatant.AddComponent<CombatantMarker>();
            SetPrivateField(marker, "faction", faction);
            SetPrivateField(marker, "combatantId", combatantId);
            combatant.AddComponent<HealthComponent>().EnterDemo();
            return combatant;
        }

        private GameObject Track(GameObject gameObject)
        {
            objects.Add(gameObject);
            return gameObject;
        }

        private static void SetCurrentHealth(HealthComponent health, float value)
        {
            PropertyInfo property = typeof(HealthComponent).GetProperty("CurrentHealth", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            property.SetValue(health, value);
        }

        private static void SetPrivateField(Object target, string fieldName, object value)
        {
            SerializedObject serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(fieldName);
            Assert.That(property, Is.Not.Null, $"Serialized field {fieldName} が見つかりません。");
            if (value is Object unityObject)
            {
                property.objectReferenceValue = unityObject;
            }
            else if (value is CombatantMarker.CombatantFaction faction)
            {
                property.enumValueIndex = (int)faction;
            }
            else if (value is string text)
            {
                property.stringValue = text;
            }
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
