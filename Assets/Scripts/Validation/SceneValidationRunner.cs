using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace TinyAdventure
{
    /// <summary>
    /// SampleSceneの実行時接続を一度だけ検証し、修正可能な診断を提供します。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SceneValidationRunner : MonoBehaviour
    {
        [Header("必須参照")]
        [SerializeField]
        private SceneReferenceRegistry sceneReferenceRegistry;

        [SerializeField]
        private GameFlowController gameFlowController;

        [SerializeField]
        private GameplayClock gameplayClock;

        [SerializeField]
        private ThirdPersonCameraController cameraController;

        [SerializeField]
        private Camera mainCamera;

        [SerializeField]
        private GameObject hudRoot;

        [SerializeField]
        private CombatantMarker player;

        [SerializeField]
        private List<CombatantMarker> configuredEnemies = new List<CombatantMarker>();

        private readonly List<string> diagnostics = new List<string>();
        private bool validationCompleted;

        public bool LastValidationPassed { get; private set; }
        public IReadOnlyList<string> Diagnostics => diagnostics;
        public string LastDiagnostic { get; private set; } = string.Empty;

        private void Awake()
        {
            ResolveReferences();
        }

        private void Start()
        {
            ValidateNow();
        }

        /// <summary>シーン接続を検証します。実行中に毎フレーム呼び出しません。</summary>
        public bool ValidateNow()
        {
            ResolveReferences();
            diagnostics.Clear();
            LastDiagnostic = string.Empty;

            Require(sceneReferenceRegistry != null, "VAL-001", "GameRootのSceneReferenceRegistry参照がありません。修正案: GameRootのRegistry欄にSceneReferenceRegistryを設定してください。");
            Require(gameFlowController != null, "VAL-002", "GameRootのGameFlowController参照がありません。修正案: GameRootのFlow欄にGameFlowControllerを設定してください。");
            Require(gameplayClock != null, "VAL-003", "GameRootのGameplayClock参照がありません。修正案: GameRootのClock欄にGameplayClockを設定してください。");
            Require(cameraController != null && cameraController.isActiveAndEnabled, "VAL-004", "活動中のCinemachine第三人称リグ参照がありません。修正案: Camera/CM_ThirdPersonを活動状態にしてください。");
            Require(mainCamera != null && mainCamera.isActiveAndEnabled, "VAL-005", "活動中のMainCamera参照がありません。修正案: Camera/MainCameraとCinemachineBrainを確認してください。");
            Require(hudRoot != null && hudRoot.activeInHierarchy && hudRoot.GetComponent<DemoHudController>() != null, "VAL-006", "活動中のHUDRootまたはDemoHudControllerがありません。修正案: UI/HUDRootのPrefab接続を確認してください。");
            Require(player != null && player.gameObject.activeInHierarchy && player.Faction == CombatantMarker.CombatantFaction.Player, "VAL-007", "活動中のPlayer参照が不正です。修正案: Player/KnightのCombatantMarkerを接続してください。");

            if (configuredEnemies == null)
            {
                configuredEnemies = new List<CombatantMarker>();
            }

            var seenEnemies = new HashSet<CombatantMarker>();
            int activeEnemyCount = 0;
            for (int index = 0; index < configuredEnemies.Count; index++)
            {
                CombatantMarker enemy = configuredEnemies[index];
                bool validEnemy = enemy != null && enemy.gameObject.activeInHierarchy && enemy.Faction == CombatantMarker.CombatantFaction.Enemy && seenEnemies.Add(enemy);
                Require(validEnemy, "VAL-008", $"敵参照 #{index + 1} が空、重複、失活、またはEnemy陣営ではありません。修正案: Enemies配下の設定済みPrefabを確認してください。");
                if (validEnemy)
                {
                    activeEnemyCount++;
                }
            }

            Require(activeEnemyCount >= 3, "VAL-009", $"活動中の設定済み敵が3体未満です（検出: {activeEnemyCount}）。修正案: Enemies配下にEnemy_Meleeを3体以上配置してください。");

            if (sceneReferenceRegistry != null)
            {
                Require(sceneReferenceRegistry.ResolveSceneReferences() && sceneReferenceRegistry.IsConfigurationValid, "VAL-010", "SceneReferenceRegistryのシーン参照が不正です。修正案: Player、DamageService、HUDRoot、Cinemachineリグと敵集合を接続してください。");
                Require(sceneReferenceRegistry.CameraRig != null && sceneReferenceRegistry.CameraRig == cameraController.gameObject, "VAL-011", "SceneReferenceRegistryのCinemachineリグ参照が一致しません。修正案: Camera/CM_ThirdPersonを設定してください。");
                Require(sceneReferenceRegistry.HudRoot == hudRoot, "VAL-012", "SceneReferenceRegistryのHUDRoot参照が一致しません。修正案: UI/HUDRootを設定してください。");
            }

            if (player != null)
            {
                Require(player.GetComponent<HealthComponent>() != null && player.GetComponent<PlayerController>() != null && player.GetComponent<PlayerCombatController>() != null, "VAL-013", "Playerに必須の体力、移動、攻撃コンポーネントがありません。修正案: Knight Prefabから再配置してください。");
                NavMeshHit hit;
                Require(NavMesh.SamplePosition(player.transform.position, out hit, 1.5f, NavMesh.AllAreas), "VAL-014", "Playerの開始位置がNavMesh上にありません。修正案: PlayerをPlayableFloor上へ移動して再ベイクしてください。");
            }

            if (cameraController != null)
            {
                Require(cameraController.GetComponent("Unity.Cinemachine.CinemachineCamera") != null && cameraController.GetComponent("Unity.Cinemachine.CinemachineOrbitalFollow") != null, "VAL-015", "Cinemachine第三人称リグの必須コンポーネントがありません。修正案: CM_ThirdPersonのCinemachine構成を確認してください。");
            }

#if UNITY_EDITOR
            var dependency = CinemachineDependencyChecker.Validate();
            Require(dependency.IsValid, "VAL-016", dependency.Diagnostic);
            Require(SceneManager.GetActiveScene().path == "Assets/Scenes/SampleScene.unity", "VAL-017", "SampleSceneが実行中の入口シーンではありません。修正案: Build Settingsの先頭にSampleSceneを設定してください。");
#endif

            validationCompleted = true;
            LastValidationPassed = diagnostics.Count == 0;
            if (!LastValidationPassed)
            {
                LastDiagnostic = diagnostics[0];
                Debug.LogError("[シーン検証] " + LastDiagnostic, this);
            }

            return LastValidationPassed;
        }

        private void ResolveReferences()
        {
            if (sceneReferenceRegistry == null) sceneReferenceRegistry = GetComponent<SceneReferenceRegistry>();
            if (gameFlowController == null) gameFlowController = GetComponent<GameFlowController>();
            if (gameplayClock == null) gameplayClock = GetComponent<GameplayClock>();
            if (cameraController == null) cameraController = FindAnyObjectByType<ThirdPersonCameraController>();
            if (mainCamera == null) mainCamera = Camera.main;
            if (hudRoot == null) hudRoot = GameObject.Find("UI/HUDRoot");
            if (player == null)
            {
                CombatantMarker[] markers = FindObjectsByType<CombatantMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (int index = 0; index < markers.Length; index++)
                {
                    if (markers[index] != null && markers[index].Faction == CombatantMarker.CombatantFaction.Player)
                    {
                        player = markers[index];
                        break;
                    }
                }
            }

            if (configuredEnemies == null) configuredEnemies = new List<CombatantMarker>();
            if (configuredEnemies.Count == 0)
            {
                CombatantMarker[] markers = FindObjectsByType<CombatantMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (int index = 0; index < markers.Length; index++)
                {
                    CombatantMarker marker = markers[index];
                    if (marker != null && marker.Faction == CombatantMarker.CombatantFaction.Enemy && !configuredEnemies.Contains(marker)) configuredEnemies.Add(marker);
                }
            }
        }

        private void Require(bool condition, string id, string diagnostic)
        {
            if (condition) return;
            string message = id + ": " + diagnostic;
            if (!diagnostics.Contains(message)) diagnostics.Add(message);
        }
    }
}
