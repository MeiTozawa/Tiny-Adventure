using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace TinyAdventure.Tests
{
    /// <summary>
    /// 実際のSampleSceneとHUDRoot prefabを使い、HUD初期化、イベント更新、終局文言を検証します。
    /// </summary>
    public sealed class SampleSceneHudTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("SampleScene", UnityEngine.SceneManagement.LoadSceneMode.Single);
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator SampleSceneStartsWithReadyHudAndRunningContent()
        {
            DemoHudController hud = FindHud();
            GameFlowController flow = FindFlow();
            SceneReferenceRegistry registry = FindRegistry();
            Canvas canvas = hud.GetComponent<Canvas>();
            CanvasScalerContract(canvas, hud);

            Assert.That(flow.IsHudReady, Is.True, "SampleSceneのGameFlowがHUD準備完了になっていません。");
            Assert.That(flow.CurrentState, Is.EqualTo(GameplayState.Running), "SampleSceneがRunning状態で開始していません。");
            Assert.That(hud.CurrentHealthText, Is.EqualTo("体力: 100/100"));
            Assert.That(hud.CurrentEnemyCountText, Is.EqualTo($"残りの敵: {registry.ActiveEnemyCount}"));
            Assert.That(hud.CurrentControlsText, Does.Contain("移動").And.Contain("左クリック").And.Contain("Rキー").And.Contain("Escキー"));
            Assert.That(hud.IsTerminalPanelVisible, Is.False);
            Assert.That(hud.transform.parent.name, Is.EqualTo("UI"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator PlayerDamageUpdatesHealthTextWithinNextFrame()
        {
            DemoHudController hud = FindHud();
            GameFlowController flow = FindFlow();
            SceneReferenceRegistry registry = FindRegistry();
            DamageService damageService = flow.DamageService;
            CombatantMarker player = registry.Player;
            CombatantMarker enemy = FindActiveEnemy(registry);
            HealthComponent playerHealth = player.GetComponent<HealthComponent>();
            enemy.transform.position = player.transform.position + Vector3.forward;
            Physics.SyncTransforms();

            AttackWindowTracker attackWindow = new AttackWindowTracker(enemy, 3f);
            Assert.That(attackWindow.BeginWindow(701, out string windowDiagnostic), Is.True, windowDiagnostic);
            Assert.That(DamageRequest.TryCreate(
                registry,
                enemy,
                player,
                5f,
                701,
                AttackKinds.EnemyMelee,
                player.transform.position,
                Time.timeAsDouble,
                out DamageRequest request,
                out string requestDiagnostic), Is.True, requestDiagnostic);
            Assert.That(damageService.Submit(request, attackWindow, out string damageDiagnostic), Is.True, damageDiagnostic);
            float eventTime = Time.realtimeSinceStartup;

            yield return null;

            Assert.That(Time.realtimeSinceStartup - eventTime, Is.LessThan(0.2f), "Player受傷後のHUD更新が0.2秒を超えました。");
            Assert.That(playerHealth.CurrentHealth, Is.EqualTo(95f));
            Assert.That(hud.CurrentHealthText, Is.EqualTo("体力: 95/100"));
            attackWindow.EndWindow(701);
        }

        [UnityTest]
        public IEnumerator EnemyUnregisterUpdatesCountAndVictoryPanelUsesJapaneseText()
        {
            DemoHudController hud = FindHud();
            GameFlowController flow = FindFlow();
            SceneReferenceRegistry registry = FindRegistry();
            int initialCount = registry.ActiveEnemyCount;
            CombatantMarker enemy = FindActiveEnemy(registry);
            registry.Unregister(enemy);
            Assert.That(registry.IsRegistered(enemy), Is.False, "実シーンのEnemyを登録解除できませんでした。");

            yield return null;

            Assert.That(hud.CurrentEnemyCountText, Is.EqualTo($"残りの敵: {initialCount - 1}"));
            Assert.That(flow.SetState(GameplayState.Victory).IsOk, Is.True, "実シーンをVictory状態へ遷移できませんでした。");
            yield return null;
            Assert.That(hud.DisplayedState, Is.EqualTo(GameplayState.Victory));
            Assert.That(hud.IsTerminalPanelVisible, Is.True);
            Assert.That(hud.CurrentVictoryTitleText, Is.EqualTo("勝利！"));
            Assert.That(hud.transform.Find("VictoryPanel").gameObject.activeSelf, Is.True);
            Assert.That(hud.transform.Find("VictoryPanel/VictoryRestartText").GetComponent<UnityEngine.UI.Text>().text, Is.EqualTo("Rキーで再開"));
            Assert.That(hud.transform.Find("DefeatPanel").gameObject.activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator PlayerDeathShowsDefeatPanelAndRestartInstruction()
        {
            DemoHudController hud = FindHud();
            GameFlowController flow = FindFlow();
            SceneReferenceRegistry registry = FindRegistry();
            CombatantMarker player = registry.Player;
            CombatantMarker enemy = FindActiveEnemy(registry);
            enemy.transform.position = player.transform.position + Vector3.forward;
            Physics.SyncTransforms();

            AttackWindowTracker attackWindow = new AttackWindowTracker(enemy, 3f);
            Assert.That(attackWindow.BeginWindow(702, out string windowDiagnostic), Is.True, windowDiagnostic);
            Assert.That(DamageRequest.TryCreate(
                registry,
                enemy,
                player,
                100f,
                702,
                AttackKinds.EnemyMelee,
                player.transform.position,
                Time.timeAsDouble,
                out DamageRequest request,
                out string requestDiagnostic), Is.True, requestDiagnostic);
            Assert.That(flow.DamageService.Submit(request, attackWindow, out string damageDiagnostic), Is.True, damageDiagnostic);
            attackWindow.EndWindow(702);

            yield return null;

            Assert.That(flow.CurrentState, Is.EqualTo(GameplayState.Defeat), "Player死亡後にGameFlowがDefeatへ遷移していません。");
            Assert.That(hud.DisplayedState, Is.EqualTo(GameplayState.Defeat));
            Assert.That(hud.CurrentDefeatTitleText, Is.EqualTo("敗北"));
            Assert.That(hud.transform.Find("DefeatPanel").gameObject.activeSelf, Is.True);
            Assert.That(hud.transform.Find("DefeatPanel/DefeatRestartText").GetComponent<UnityEngine.UI.Text>().text, Is.EqualTo("Rキーで再開"));
            Assert.That(hud.transform.Find("VictoryPanel").gameObject.activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator RepeatedPreparationKeepsHudReferencesAndSubscriptionsStable()
        {
            DemoHudController hud = FindHud();
            GameFlowController flow = FindFlow();
            SceneReferenceRegistry registry = FindRegistry();
            int updateCountBefore = hud.UiUpdateCount;
            Assert.That(hud.Prepare(flow, registry, out string diagnostic), Is.True, diagnostic);
            Assert.That(hud.Prepare(flow, registry, out diagnostic), Is.True, diagnostic);
            Assert.That(hud.UiUpdateCount, Is.EqualTo(updateCountBefore));
            Assert.That(hud.CurrentHealthText, Is.EqualTo("体力: 100/100"));
            yield return null;
        }

        private static DemoHudController FindHud()
        {
            GameObject hudObject = GameObject.Find("UI/HUDRoot");
            Assert.That(hudObject, Is.Not.Null, "SampleSceneにUI/HUDRootがありません。");
            DemoHudController hud = hudObject.GetComponent<DemoHudController>();
            Assert.That(hud, Is.Not.Null, "HUDRootにDemoHudControllerがありません。");
            return hud;
        }

        private static GameFlowController FindFlow()
        {
            GameFlowController flow = Object.FindAnyObjectByType<GameFlowController>();
            Assert.That(flow, Is.Not.Null, "SampleSceneにGameFlowControllerがありません。");
            return flow;
        }

        private static SceneReferenceRegistry FindRegistry()
        {
            SceneReferenceRegistry registry = Object.FindAnyObjectByType<SceneReferenceRegistry>();
            Assert.That(registry, Is.Not.Null, "SampleSceneにSceneReferenceRegistryがありません。");
            return registry;
        }

        private static CombatantMarker FindActiveEnemy(SceneReferenceRegistry registry)
        {
            foreach (CombatantMarker enemy in registry.ActiveEnemies)
            {
                if (enemy != null && enemy.gameObject.activeInHierarchy)
                {
                    return enemy;
                }
            }

            Assert.Fail("SampleSceneに活動中の敵がありません。");
            return null;
        }

        private static void CanvasScalerContract(Canvas canvas, DemoHudController hud)
        {
            Assert.That(canvas, Is.Not.Null, "HUDRootにCanvasがありません。");
            Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
            CanvasScaler scaler = hud.GetComponent<CanvasScaler>();
            Assert.That(scaler, Is.Not.Null, "HUDRootにCanvasScalerがありません。");
            Assert.That(scaler.referenceResolution, Is.EqualTo(new Vector2(1280f, 720f)));
            Assert.That(hud.GetComponent<GraphicRaycaster>(), Is.Not.Null, "HUDRootにGraphicRaycasterがありません。");
        }
    }
}
