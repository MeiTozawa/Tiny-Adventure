using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityInputSystem = UnityEngine.InputSystem.InputSystem;

namespace TinyAdventure.Tests
{
    /// <summary>
    /// 実際のSampleSceneで終局後のR再開とEditor終了要求記録を検証します。
    /// </summary>
    public sealed class SampleSceneRestartExitTests
    {
        private Keyboard keyboard;
        private bool reloadedSceneCaptured;
        private Vector3 reloadedPlayerPosition;
        private Quaternion reloadedPlayerRotation;
        private float reloadedPlayerHealth;
        private Vector3[] reloadedEnemyPositions = new Vector3[0];

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            SceneManager.LoadScene("SampleScene", LoadSceneMode.Single);
            yield return null;
            yield return null;
            LogAssert.ignoreFailingMessages = false;

            keyboard = UnityInputSystem.AddDevice<Keyboard>();
            Assert.That(keyboard, Is.Not.Null, "SampleSceneの再開・終了入力テスト用Keyboardを作成できませんでした。");
            UnityInputSystem.Update();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            if (keyboard != null && keyboard.added)
            {
                UnityInputSystem.RemoveDevice(keyboard);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator VictoryRestartViaRReloadsSampleSceneAndRestoresSpawnSnapshot()
        {
            yield return VerifyRestartRestoresSpawnSnapshot(GameplayState.Victory);
        }

        [UnityTest]
        public IEnumerator DefeatRestartViaRReloadsSampleSceneAndRestoresSpawnSnapshot()
        {
            yield return VerifyRestartRestoresSpawnSnapshot(GameplayState.Defeat);
        }

        [UnityTest]
        public IEnumerator ExitInputRecordsRequestInEditorWithoutChangingGameplayState()
        {
            GameFlowController flow = FindFlow();
            bool exitRequested = false;
            flow.ExitRequested += () => exitRequested = true;

            Assert.That(flow.SetState(GameplayState.Victory).IsOk, Is.True, "終了入力テストのVictory遷移に失敗しました。");
            GameplayState stateBeforeExit = flow.CurrentState;
            flow.ProcessInput(new GameplayInputSnapshot(Vector2.zero, Vector2.zero, false, false, true));

            Assert.That(exitRequested, Is.True, "終了要求イベントが発火していません。");
            Assert.That(flow.CurrentState, Is.EqualTo(stateBeforeExit), "終了要求でGameFlow状態が変更されました。");
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("SampleScene"), "Editorの終了要求でSampleSceneが変更されました。");
            yield return null;
        }

        private IEnumerator VerifyRestartRestoresSpawnSnapshot(GameplayState terminalState)
        {
            GameFlowController flow = FindFlow();
            SceneReferenceRegistry registry = Object.FindAnyObjectByType<SceneReferenceRegistry>();
            Assert.That(flow.ReloadSceneOnRestart, Is.True, "SampleSceneのreloadSceneOnRestartが有効ではありません。");
            Assert.That(flow.RestartSceneName, Is.EqualTo("SampleScene"), "再開対象シーンがSampleSceneではありません。");
            Assert.That(registry.IsSnapshotCaptured, Is.True, "再開前のSpawnSnapshotが保存されていません。");

            CombatantMarker player = registry.Player;
            HealthComponent playerHealth = player.GetComponent<HealthComponent>();
            Vector3 initialPlayerPosition = registry.SpawnSnapshots[player].Position;
            Quaternion initialPlayerRotation = registry.SpawnSnapshots[player].Rotation;
            float initialPlayerHealth = registry.SpawnSnapshots[player].InitialHealth;
            Vector3[] initialEnemyPositions = CaptureSpawnPositions(registry);

            Assert.That(flow.SetState(terminalState).IsOk, Is.True, $"{terminalState}状態へ遷移できませんでした。");
            player.transform.position += new Vector3(3f, 0f, 2f);
            player.transform.rotation = Quaternion.Euler(0f, 123f, 0f);
            SetCurrentHealth(playerHealth, 13f);
            MoveEnemies(registry);
            Physics.SyncTransforms();

            InputReader inputReader = Object.FindAnyObjectByType<InputReader>();
            Assert.That(inputReader, Is.Not.Null, "SampleSceneにInputReaderがありません。");
            Assert.That(inputReader.IsGameplayMapEnabled, Is.True, "Gameplayアクションマップが有効になっていません。");
            UnityInputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.R));
            UnityInputSystem.Update();
            GameplayInputSnapshot snapshot = inputReader.ReadSnapshot();
            Assert.That(snapshot.RestartPressed, Is.True, "RキーがRestart入力として読み取られていません。");
            reloadedSceneCaptured = false;
            reloadedEnemyPositions = new Vector3[0];
            SceneManager.sceneLoaded += CaptureReloadedScene;
            flow.ProcessInput(snapshot);
            UnityInputSystem.QueueStateEvent(keyboard, new KeyboardState());
            UnityInputSystem.Update();
            yield return null;
            SceneManager.sceneLoaded -= CaptureReloadedScene;

            yield return null;
            yield return null;

            GameFlowController reloadedFlow = FindFlow();
            SceneReferenceRegistry reloadedRegistry = Object.FindAnyObjectByType<SceneReferenceRegistry>();
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("SampleScene"), "R再開後のシーンがSampleSceneではありません。");
            Assert.That(reloadedFlow.CurrentState, Is.EqualTo(GameplayState.Running), "R再開後にRunningへ戻っていません。");
            Assert.That(reloadedRegistry.IsSnapshotCaptured, Is.True, "R再開後にSpawnSnapshotが再保存されていません。");
            Assert.That(reloadedRegistry.ActiveEnemyCount, Is.EqualTo(initialEnemyPositions.Length), "R再開後の敵数が初期敵数と一致しません。");

            Assert.That(reloadedSceneCaptured, Is.True, "R再開時のSampleSceneロードイベントを観測できませんでした。");
            Assert.That(Vector3.Distance(reloadedPlayerPosition, initialPlayerPosition), Is.LessThan(0.001f), "R再開時のPlayer位置がSpawnSnapshotと一致しません。");
            Assert.That(Quaternion.Angle(reloadedPlayerRotation, initialPlayerRotation), Is.LessThan(0.1f), "R再開時のPlayer回転がSpawnSnapshotと一致しません。");
            Assert.That(reloadedPlayerHealth, Is.EqualTo(initialPlayerHealth), "R再開時のPlayer体力が初期値と一致しません。");

            Assert.That(reloadedEnemyPositions.Length, Is.EqualTo(initialEnemyPositions.Length), "R再開時に観測した敵数がSpawnSnapshotと一致しません。");
            for (int index = 0; index < initialEnemyPositions.Length; index++)
            {
                CombatantMarker reloadedEnemy = reloadedRegistry.ConfiguredEnemies[index];
                Assert.That(reloadedRegistry.SpawnSnapshots[reloadedEnemy].Position, Is.EqualTo(initialEnemyPositions[index]), $"R再開後の敵#{index + 1} SpawnSnapshot位置が初期値と一致しません。");
                Assert.That(Vector3.Distance(reloadedEnemyPositions[index], initialEnemyPositions[index]), Is.LessThan(0.001f), $"R再開時の敵#{index + 1}位置がSpawnSnapshotと一致しません。");
            }
        }

        private void CaptureReloadedScene(Scene scene, LoadSceneMode mode)
        {
            if (!string.Equals(scene.name, "SampleScene", System.StringComparison.Ordinal))
            {
                return;
            }

            SceneReferenceRegistry registry = Object.FindAnyObjectByType<SceneReferenceRegistry>();
            if (registry == null || registry.Player == null)
            {
                return;
            }

            CombatantMarker player = registry.Player;
            HealthComponent health = player.GetComponent<HealthComponent>();
            reloadedPlayerPosition = player.transform.position;
            reloadedPlayerRotation = player.transform.rotation;
            reloadedPlayerHealth = health != null ? health.CurrentHealth : 0f;
            var positions = new System.Collections.Generic.List<Vector3>();
            for (int index = 0; index < registry.ConfiguredEnemies.Count; index++)
            {
                CombatantMarker enemy = registry.ConfiguredEnemies[index];
                if (enemy != null && enemy.gameObject.activeInHierarchy)
                {
                    positions.Add(enemy.transform.position);
                }
            }

            reloadedEnemyPositions = positions.ToArray();
            reloadedSceneCaptured = true;
        }

        private static GameFlowController FindFlow()
        {
            GameFlowController flow = Object.FindAnyObjectByType<GameFlowController>();
            Assert.That(flow, Is.Not.Null, "SampleSceneにGameFlowControllerがありません。");
            Assert.That(flow.CurrentState, Is.EqualTo(GameplayState.Running));
            return flow;
        }

        private static Vector3[] CaptureSpawnPositions(SceneReferenceRegistry registry)
        {
            var positions = new System.Collections.Generic.List<Vector3>();
            for (int index = 0; index < registry.ConfiguredEnemies.Count; index++)
            {
                CombatantMarker enemy = registry.ConfiguredEnemies[index];
                if (enemy != null && registry.SpawnSnapshots.TryGetValue(enemy, out SpawnSnapshot snapshot))
                {
                    positions.Add(snapshot.Position);
                }
            }

            return positions.ToArray();
        }

        private static void MoveEnemies(SceneReferenceRegistry registry)
        {
            for (int index = 0; index < registry.ConfiguredEnemies.Count; index++)
            {
                CombatantMarker enemy = registry.ConfiguredEnemies[index];
                if (enemy != null && enemy.gameObject.activeInHierarchy)
                {
                    enemy.transform.position += new Vector3(-2f, 0f, 1f);
                }
            }
        }

        private static void SetCurrentHealth(HealthComponent health, float value)
        {
            PropertyInfo property = typeof(HealthComponent).GetProperty("CurrentHealth", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(property, Is.Not.Null, "HealthComponent.CurrentHealthプロパティが見つかりません。");
            property.SetValue(health, value);
        }
    }
}
