using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.PackageManager;
#endif

namespace TinyAdventure
{
    /// <summary>検証項目の重大度です。</summary>
    public enum ValidationIssueSeverity
    {
        Warning,
        Error
    }

    /// <summary>一件の検証失敗または注意を安定した形式で保持します。</summary>
    public sealed class ValidationIssue
    {
        public ValidationIssue(string stableId, string targetName, string repairSuggestion, string diagnostic, ValidationIssueSeverity severity)
        {
            StableId = stableId ?? string.Empty;
            TargetName = targetName ?? string.Empty;
            RepairSuggestion = repairSuggestion ?? string.Empty;
            Diagnostic = diagnostic ?? string.Empty;
            Severity = severity;
        }

        public string StableId { get; }
        public string TargetName { get; }
        public string RepairSuggestion { get; }
        public string Diagnostic { get; }
        public ValidationIssueSeverity Severity { get; }
        public bool IsError => Severity == ValidationIssueSeverity.Error;

        public override string ToString()
        {
            string level = IsError ? "失敗" : "注意";
            return $"[{level}][{StableId}] 対象「{TargetName}」: {Diagnostic} 修正案: {RepairSuggestion}";
        }
    }

    /// <summary>SampleScene検証の全結果です。</summary>
    public sealed class ValidationReport
    {
        private readonly List<ValidationIssue> issues = new List<ValidationIssue>();
        private int checkedCount;

        public IReadOnlyList<ValidationIssue> Issues => issues;
        public int CheckedCount => checkedCount;
        public bool IsValid
        {
            get
            {
                for (int index = 0; index < issues.Count; index++)
                {
                    if (issues[index].IsError)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public int ErrorCount
        {
            get
            {
                int count = 0;
                for (int index = 0; index < issues.Count; index++)
                {
                    if (issues[index].IsError)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public int WarningCount
        {
            get
            {
                int count = 0;
                for (int index = 0; index < issues.Count; index++)
                {
                    if (!issues[index].IsError)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public void AddCheck()
        {
            checkedCount++;
        }

        public void AddError(string stableId, string targetName, string repairSuggestion, string diagnostic)
        {
            AddIssue(stableId, targetName, repairSuggestion, diagnostic, ValidationIssueSeverity.Error);
        }

        public void AddWarning(string stableId, string targetName, string repairSuggestion, string diagnostic)
        {
            AddIssue(stableId, targetName, repairSuggestion, diagnostic, ValidationIssueSeverity.Warning);
        }

        public string ToJapaneseSummary()
        {
            return $"検証結果: {(IsValid ? "成功" : "失敗")}、確認 {CheckedCount}件、失敗 {ErrorCount}件、注意 {WarningCount}件。";
        }

        private void AddIssue(string stableId, string targetName, string repairSuggestion, string diagnostic, ValidationIssueSeverity severity)
        {
            issues.Add(new ValidationIssue(stableId, targetName, repairSuggestion, diagnostic, severity));
        }
    }

    /// <summary>
    /// SampleSceneの依存、階層、リソース、数値、NavMesh、衝突行列、参照を順序通りに検査します。
    /// </summary>
    public sealed class DemoSceneValidator
    {
        private const string SampleSceneName = "SampleScene";
        private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";
        private const string RequiredCinemachineVersion = "3.1.6";
        private const string RequiredInputVersion = "1.20.0";
        private const string RequiredNavigationVersion = "2.0.14";
        private const string RequiredTestFrameworkVersion = "1.7.0";
        private const float NavMeshSampleDistance = 1.5f;
        private const float PlayableAreaMargin = 0.75f;

        private readonly BindingFlags instanceFieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>現在のアクティブシーンを検査します。</summary>
        public ValidationReport ValidateCurrentScene()
        {
            return Validate(SceneManager.GetActiveScene());
        }

        /// <summary>指定シーンを検査し、失敗項目をすべて収集します。</summary>
        public ValidationReport Validate(Scene scene)
        {
            ValidationReport report = new ValidationReport();
            if (!scene.IsValid())
            {
                report.AddError(
                    "SCN-SCENE-001",
                    SampleSceneName,
                    "SampleSceneを開き、有効なシーンとして保存してください。",
                    "検証対象のシーンが無効です。");
                return report;
            }

            ValidateDependencies(report);
            ValidateBuildSettings(report, scene);

            SceneReferenceRegistry registry = FindComponent<SceneReferenceRegistry>(scene);
            GameFlowController flow = FindComponent<GameFlowController>(scene);
            GameplayClock clock = FindComponent<GameplayClock>(scene);
            DamageService damageService = FindComponent<DamageService>(scene);
            ValidateCoreReferences(report, registry, flow, clock, damageService);

            CombatantMarker player = ValidatePlayer(report, scene, registry);
            List<CombatantMarker> enemies = ValidateEnemies(report, scene, registry, player);
            GameObject hudRoot = ValidateHud(report, scene, registry, player);
            ValidateCamera(report, scene, registry, player);
            ValidateNumbers(report, player, enemies);
            ValidateNavigation(report, scene, player, enemies);
            ValidateCollisionMatrix(report, player, enemies);
            ValidateSceneReferences(report, registry, flow, clock, damageService, hudRoot, player, enemies);
            JapaneseTextAudit.AuditScene(scene, report, hudRoot);
            return report;
        }

        private void ValidateDependencies(ValidationReport report)
        {
            ValidatePackage(report, "SCN-DEP-CINEMACHINE", "com.unity.cinemachine", RequiredCinemachineVersion, "Cinemachineパッケージを解決してください。正式な第三人称リグを普通のCameraで代替しないでください。");
            ValidatePackage(report, "SCN-DEP-INPUT", "com.unity.inputsystem", RequiredInputVersion, "Input Systemパッケージを解決し、Gameplay入力アクションを有効にしてください。");
            ValidatePackage(report, "SCN-DEP-NAVIGATION", "com.unity.ai.navigation", RequiredNavigationVersion, "AI Navigationパッケージを解決し、NavMeshSurfaceを配置してください。");
            ValidatePackage(report, "SCN-DEP-TEST", "com.unity.test-framework", RequiredTestFrameworkVersion, "Test Frameworkパッケージを解決し、検証テストを実行可能にしてください。");
        }

        private void ValidateBuildSettings(ValidationReport report, Scene scene)
        {
#if UNITY_EDITOR
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            int enabledSampleSceneCount = 0;
            int firstEnabledSampleSceneIndex = -1;
            for (int index = 0; index < scenes.Length; index++)
            {
                EditorBuildSettingsScene buildScene = scenes[index];
                if (buildScene == null || !buildScene.enabled || !string.Equals(buildScene.path, SampleScenePath, StringComparison.Ordinal))
                {
                    continue;
                }

                enabledSampleSceneCount++;
                if (firstEnabledSampleSceneIndex < 0)
                {
                    firstEnabledSampleSceneIndex = index;
                }
            }

            if (enabledSampleSceneCount == 0)
            {
                report.AddError(
                    "SCN-BUILD-SAMPLE-001",
                    SampleSceneName,
                    "Build SettingsへAssets/Scenes/SampleScene.unityを有効な入口シーンとして追加してください。",
                    "SampleSceneがBuild Settingsの有効な入口として登録されていません。いずれかの実行入口が必要です。");
            }
            else if (enabledSampleSceneCount > 1)
            {
                report.AddError(
                    "SCN-BUILD-SAMPLE-003",
                    SampleSceneName,
                    "Build SettingsではAssets/Scenes/SampleScene.unityを有効な入口として一件だけ登録してください。重複した項目を削除してください。",
                    $"SampleSceneがBuild Settingsに{enabledSampleSceneCount}件の有効な重複項目として登録されています。唯一の入口が必要です。");
            }
            else
            {
                report.AddCheck();
            }

            if (enabledSampleSceneCount == 1 && firstEnabledSampleSceneIndex != 0)
            {
                report.AddError(
                    "SCN-BUILD-SAMPLE-004",
                    SampleSceneName,
                    "Build SettingsでSampleSceneを有効な入口の先頭（Build Index 0）へ移動してください。",
                    $"SampleSceneのBuild Indexが{firstEnabledSampleSceneIndex}です。Demo入口はBuild Index 0である必要があります。");
            }
            else if (enabledSampleSceneCount == 1)
            {
                report.AddCheck();
            }
#else
            report.AddWarning(
                "SCN-BUILD-SAMPLE-EDITOR",
                SampleSceneName,
                "Unity EditorでBuild Settingsを検証してください。",
                "Build Settingsの検査はUnity Editorでのみ実行できます。");
#endif

            if (!string.Equals(scene.name, SampleSceneName, StringComparison.Ordinal))
            {
                report.AddError(
                    "SCN-BUILD-SAMPLE-002",
                    scene.name,
                    "SampleSceneを開いてからDemoを実行してください。",
                    "現在のシーンがSampleSceneではありません。");
            }
            else
            {
                report.AddCheck();
            }
        }

        private void ValidateCoreReferences(
            ValidationReport report,
            SceneReferenceRegistry registry,
            GameFlowController flow,
            GameplayClock clock,
            DamageService damageService)
        {
            AddReferenceResult(report, "SCN-REF-REGISTRY", "GameRoot", registry, "GameRootへSceneReferenceRegistryを追加し、Player、敵、HUD、CameraRigを接続してください。");
            AddReferenceResult(report, "SCN-REF-FLOW", "GameRoot", flow, "GameRootへGameFlowControllerを追加し、SceneReferenceRegistry、GameplayClock、DamageServiceを接続してください。");
            AddReferenceResult(report, "SCN-REF-CLOCK", "GameRoot", clock, "GameRootへGameplayClockを追加してください。");
            AddReferenceResult(report, "SCN-REF-DAMAGE", "GameRoot", damageService, "GameRootへDamageServiceを追加し、GameFlowControllerから参照してください。");

            if (registry != null)
            {
                if (!registry.ResolveSceneReferences())
                {
                    report.AddError(
                        "SCN-REF-REGISTRY-CONFIG",
                        registry.name,
                        "SceneReferenceRegistryのPlayer、DamageService、敵リストの空参照と重複を修正してください。",
                        string.IsNullOrEmpty(registry.LastDiagnostic) ? "SceneReferenceRegistryの設定が不正です。" : registry.LastDiagnostic);
                }
                else
                {
                    report.AddCheck();
                }
            }
        }

        private CombatantMarker ValidatePlayer(ValidationReport report, Scene scene, SceneReferenceRegistry registry)
        {
            List<CombatantMarker> activePlayers = new List<CombatantMarker>();
            CombatantMarker[] markers = FindComponents<CombatantMarker>(scene);
            for (int index = 0; index < markers.Length; index++)
            {
                CombatantMarker marker = markers[index];
                if (marker != null && marker.Faction == CombatantMarker.CombatantFaction.Player && marker.gameObject.activeInHierarchy)
                {
                    activePlayers.Add(marker);
                }
            }

            if (activePlayers.Count != 1)
            {
                report.AddError(
                    "SCN-PLAYER-UNIQUE-001",
                    activePlayers.Count == 0 ? "Player" : activePlayers[0].gameObject.name,
                    "シーンには活動中のPlayer陣営Knightを一体だけ配置し、他のPlayerマーカーを削除または無効化してください。",
                    $"活動中のPlayer陣営Knightが{activePlayers.Count}体あります。唯一のPlayerが必要です。");
            }
            else
            {
                report.AddCheck();
            }

            CombatantMarker player = activePlayers.Count > 0 ? activePlayers[0] : null;
            if (registry != null && player != null && registry.Player != player)
            {
                report.AddError(
                    "SCN-REF-PLAYER-001",
                    registry.name,
                    "SceneReferenceRegistry.Playerへ活動中の唯一のKnightを接続してください。",
                    "SceneReferenceRegistryのPlayer参照が唯一の活動Playerと一致しません。");
            }
            else if (player != null)
            {
                report.AddCheck();
            }

            if (player == null)
            {
                return null;
            }

            AddComponentResult(report, "SCN-PLAYER-COMPONENT-CHARACTER", player.gameObject, player.GetComponent<CharacterController>(), "KnightへCharacterControllerを追加してください。");
            AddComponentResult(report, "SCN-PLAYER-COMPONENT-INPUT", player.gameObject, player.GetComponent<InputReader>(), "KnightへInputReaderを追加してください。");
            AddComponentResult(report, "SCN-PLAYER-COMPONENT-MOVE", player.gameObject, player.GetComponent<PlayerController>(), "KnightへPlayerControllerを追加してください。");
            AddComponentResult(report, "SCN-PLAYER-COMPONENT-COMBAT", player.gameObject, player.GetComponent<PlayerCombatController>(), "KnightへPlayerCombatControllerを追加してください。");
            AddComponentResult(report, "SCN-PLAYER-COMPONENT-ANIMATION", player.gameObject, player.GetComponent<PlayerAnimationDriver>(), "KnightへPlayerAnimationDriverを追加してください。");
            AddComponentResult(report, "SCN-PLAYER-COMPONENT-HEALTH", player.gameObject, player.GetComponent<HealthComponent>(), "KnightへHealthComponentを追加し、最大体力を正の値に設定してください。");

            Animator animator = player.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                report.AddError(
                    "SCN-PLAYER-ANIMATOR-001",
                    player.gameObject.name,
                    "KnightのModelRootへAnimatorとKayKit Animator Controllerを接続してください。",
                    "KnightのAnimatorまたはAnimator Controllerがありません。");
            }
            else
            {
                report.AddCheck();
            }

            Transform modelRoot = FindDescendant(player.transform, "ModelRoot");
            Renderer modelRenderer = modelRoot != null ? modelRoot.GetComponentInChildren<Renderer>(true) : null;
            if (modelRoot == null || modelRenderer == null)
            {
                report.AddError(
                    "SCN-PLAYER-KAYKIT-001",
                    player.gameObject.name,
                    "Knight配下へKayKitモデルをModelRootとして配置し、Rendererを有効にしてください。",
                    "KnightのKayKitモデルまたは表示Rendererが見つかりません。");
            }
            else
            {
                report.AddCheck();
            }

            ValidatePlayerSword(report, player);
            return player;
        }

        private void ValidatePlayerSword(ValidationReport report, CombatantMarker player)
        {
            Transform swordSocket = FindDescendant(player.transform, "SwordSocket");
            Transform swordVisual = swordSocket != null ? FindDescendant(swordSocket, "SwordVisual") : null;
            CombatHitbox swordHitbox = swordSocket != null ? FindDescendantComponent<CombatHitbox>(swordSocket) : null;
            Collider collider = swordHitbox != null ? swordHitbox.GetComponent<Collider>() : null;
            Renderer renderer = swordVisual != null ? swordVisual.GetComponentInChildren<Renderer>(true) : null;
            SwordTrailController swordTrailController = swordSocket != null ? FindDescendantComponent<SwordTrailController>(swordSocket) : null;
            TrailRenderer trailRenderer = swordTrailController != null ? swordTrailController.GetComponent<TrailRenderer>() : null;

            if (swordSocket == null)
            {
                report.AddError(
                    "SCN-PLAYER-SWORD-SOCKET-001",
                    player.gameObject.name,
                    "Knightの実際の手骨へSwordSocketを配置してください。骨名のハードコードは避けてください。",
                    "KnightのSwordSocketが見つかりません。");
            }
            else
            {
                report.AddCheck();
            }

            if (swordVisual == null || renderer == null)
            {
                report.AddError(
                    "SCN-PLAYER-SWORD-VISUAL-001",
                    swordSocket == null ? player.gameObject.name : swordSocket.name,
                    "SwordSocket配下へSwordVisualと表示可能なRendererを配置してください。",
                    "Knightの可視SwordVisualが見つかりません。");
            }
            else
            {
                report.AddCheck();
            }

            if (swordHitbox == null || collider == null || !collider.isTrigger)
            {
                report.AddError(
                    "SCN-PLAYER-SWORD-HITBOX-001",
                    swordSocket == null ? player.gameObject.name : swordSocket.name,
                    "SwordSocket配下のSwordHitboxへCombatHitboxとTrigger Colliderを接続してください。",
                    "KnightのSwordHitboxまたはTrigger Colliderが不正です。");
            }
            else
            {
                report.AddCheck();
            }

            if (swordTrailController == null || trailRenderer == null || trailRenderer.sharedMaterial == null || (trailRenderer.sharedMaterial.shader != null && trailRenderer.sharedMaterial.shader.name == "Hidden/InternalErrorShader"))
            {
                report.AddError(
                    "SCN-PLAYER-SWORD-TRAIL-001",
                    swordSocket == null ? player.gameObject.name : swordSocket.name,
                    "SwordSocket配下のSwordTrailへSwordTrailControllerと有効なMaterialを設定したTrailRendererを配置してください。",
                    "KnightのSwordTrailまたはTrailRendererのMaterialが不正です。");
            }
            else
            {
                report.AddCheck();
            }
        }

        private List<CombatantMarker> ValidateEnemies(ValidationReport report, Scene scene, SceneReferenceRegistry registry, CombatantMarker player)
        {
            List<CombatantMarker> enemies = new List<CombatantMarker>();
            CombatantMarker[] markers = FindComponents<CombatantMarker>(scene);
            for (int index = 0; index < markers.Length; index++)
            {
                CombatantMarker marker = markers[index];
                if (marker != null && marker.Faction == CombatantMarker.CombatantFaction.Enemy && marker.gameObject.activeInHierarchy)
                {
                    enemies.Add(marker);
                }
            }

            if (enemies.Count < 3)
            {
                report.AddError(
                    "SCN-ENEMY-COUNT-001",
                    "Enemies",
                    "Enemies配下へ同じ敵Prefabから3体以上の活動中敵を配置してください。",
                    $"活動中の敵が{enemies.Count}体しかありません。3体以上必要です。");
            }
            else
            {
                report.AddCheck();
            }

            for (int index = 0; index < enemies.Count; index++)
            {
                ValidateEnemy(report, enemies[index], index);
            }

            if (registry != null)
            {
                IReadOnlyList<CombatantMarker> configured = registry.ConfiguredEnemies;
                if (configured == null || configured.Count < 3)
                {
                    report.AddError(
                        "SCN-ENEMY-REGISTRY-001",
                        registry.name,
                        "SceneReferenceRegistry.ConfiguredEnemiesへ3体以上の敵参照を設定してください。",
                        "SceneReferenceRegistryに設定済み敵が3体未満です。");
                }
                else
                {
                    report.AddCheck();
                }
            }

            return enemies;
        }

        private void ValidateEnemy(ValidationReport report, CombatantMarker enemy, int index)
        {
            string prefix = "SCN-ENEMY-" + (index + 1).ToString("00") + "-";
            string targetName = enemy.gameObject.name;
            AddComponentResult(report, prefix + "MARKER", enemy.gameObject, enemy, "敵へEnemy陣営のCombatantMarkerを接続してください。");
            AddComponentResult(report, prefix + "HEALTH", enemy.gameObject, enemy.GetComponent<HealthComponent>(), "敵へHealthComponentを追加し、最大体力を正の値に設定してください。");
            AddComponentResult(report, prefix + "AGENT", enemy.gameObject, enemy.GetComponent<NavMeshAgent>(), "敵へNavMeshAgentを追加し、正の速度と有効な停止距離を設定してください。");
            AddComponentResult(report, prefix + "BRAIN", enemy.gameObject, enemy.GetComponent<EnemyBrain>(), "敵へEnemyBrainを追加してください。");
            AddComponentResult(report, prefix + "COMBAT", enemy.gameObject, enemy.GetComponent<EnemyMeleeCombat>(), "敵へEnemyMeleeCombatを追加してください。");
            AddComponentResult(report, prefix + "ANIMATION", enemy.gameObject, enemy.GetComponent<EnemyAnimationDriver>(), "敵へEnemyAnimationDriverを追加してください。");
            AddComponentResult(report, prefix + "LIFECYCLE", enemy.gameObject, enemy.GetComponent<EnemyLifecycle>(), "敵へEnemyLifecycleを追加してください。");

            Animator animator = enemy.GetComponentInChildren<Animator>(true);
            Renderer renderer = enemy.GetComponentInChildren<Renderer>(true);
            if (animator == null || animator.runtimeAnimatorController == null || renderer == null)
            {
                report.AddError(
                    prefix + "KAYKIT",
                    targetName,
                    "敵のModelRootへKayKitモデル、Animator Controller、Rendererを接続してください。",
                    $"敵「{targetName}」のKayKit表示またはAnimator参照が不正です。");
            }
            else
            {
                report.AddCheck();
            }

            Transform weaponSocket = FindDescendant(enemy.transform, "WeaponSocket");
            CombatHitbox weaponHitbox = weaponSocket != null ? FindDescendantComponent<CombatHitbox>(weaponSocket) : null;
            Collider weaponCollider = weaponHitbox != null ? weaponHitbox.GetComponent<Collider>() : null;
            if (weaponSocket == null || weaponHitbox == null || weaponCollider == null || !weaponCollider.isTrigger)
            {
                report.AddError(
                    prefix + "WEAPON-HITBOX",
                    targetName,
                    "敵のWeaponSocket配下へEnemyHitbox、CombatHitbox、Trigger Colliderを接続してください。",
                    $"敵「{targetName}」のWeaponSocketまたはEnemyHitboxが不正です。");
            }
            else
            {
                report.AddCheck();
            }
        }

        private GameObject ValidateHud(ValidationReport report, Scene scene, SceneReferenceRegistry registry, CombatantMarker player)
        {
            GameObject hudRoot = FindGameObjectByPath(scene, "UI/HUDRoot");
            AddReferenceResult(report, "SCN-HUD-ROOT-001", "UI/HUDRoot", hudRoot, "UI/HUDRootへUGUI CanvasとDemoHudControllerを配置してください。");
            if (hudRoot == null)
            {
                return null;
            }

            Canvas canvas = hudRoot.GetComponent<Canvas>();
            DemoHudController hud = hudRoot.GetComponent<DemoHudController>();
            AddComponentResult(report, "SCN-HUD-CANVAS-001", hudRoot, canvas, "HUDRootへUGUI Canvasを追加してください。");
            AddComponentResult(report, "SCN-HUD-CONTROLLER-001", hudRoot, hud, "HUDRootへDemoHudControllerを追加してください。");

            string[] requiredFields =
            {
                "healthText", "enemyCountText", "controlsText", "victoryPanel", "defeatPanel",
                "victoryTitleText", "victoryRestartText", "defeatTitleText", "defeatRestartText"
            };
            for (int index = 0; index < requiredFields.Length; index++)
            {
                bool assigned = hud != null && GetFieldValue(hud, requiredFields[index]) != null;
                if (!assigned)
                {
                    report.AddError(
                        "SCN-HUD-REF-" + requiredFields[index],
                        hudRoot.name,
                        "DemoHudControllerへ体力、敵数、操作案内、勝敗パネルと日本語Text参照を接続してください。",
                        $"HUDRootの参照「{requiredFields[index]}」が空です。");
                }
                else
                {
                    report.AddCheck();
                }
            }

            if (registry != null && registry.HudRoot != hudRoot)
            {
                report.AddError(
                    "SCN-REF-HUD-001",
                    registry.name,
                    "SceneReferenceRegistry.HudRootへUI/HUDRootを接続してください。",
                    "SceneReferenceRegistryのHUDRoot参照が実際のHUDRootと一致しません。");
            }
            else if (registry != null)
            {
                report.AddCheck();
            }

            return hudRoot;
        }

        private void ValidateCamera(ValidationReport report, Scene scene, SceneReferenceRegistry registry, CombatantMarker player)
        {
            GameObject rigObject = FindGameObjectByPath(scene, "Camera/CM_FirstPerson");
            AddReferenceResult(report, "SCN-CAMERA-RIG-001", "Camera/CM_FirstPerson", rigObject, "Camera/CM_FirstPersonへ正式なCinemachine第一人称リグを配置してください。");
            if (rigObject == null)
            {
                return;
            }

            FirstPersonCameraController controller = rigObject.GetComponent<FirstPersonCameraController>();
            Component rig = rigObject.GetComponent("CinemachineCamera");
            Component hardLock = rigObject.GetComponent("CinemachineHardLockToTarget");
            Component panTilt = rigObject.GetComponent("CinemachinePanTilt");
            Component impulseListener = rigObject.GetComponent("CinemachineImpulseListener");
            AddComponentResult(report, "SCN-CAMERA-CONTROLLER-001", rigObject, controller, "CM_FirstPersonへFirstPersonCameraControllerを追加してください。");
            AddComponentResult(report, "SCN-CAMERA-CINEMACHINE-001", rigObject, rig, "CM_FirstPersonへCinemachineCameraを追加してください。");
            AddComponentResult(report, "SCN-CAMERA-HARDLOCK-001", rigObject, hardLock, "CM_FirstPersonへCinemachineHardLockToTargetを追加してください。");
            AddComponentResult(report, "SCN-CAMERA-PANTILT-001", rigObject, panTilt, "CM_FirstPersonへCinemachinePanTiltを追加してください。");
            AddComponentResult(report, "SCN-CAMERA-IMPULSE-001", rigObject, impulseListener, "CM_FirstPersonへCinemachineImpulseListenerを追加してください。");

            if (controller != null && player != null)
            {
                controller.ResolvePlayerCameraTarget();
                Transform expectedTarget = FindDescendant(player.transform, "CameraTarget");
                if (expectedTarget == null || controller.PlayerCameraTarget != expectedTarget)
                {
                    report.AddError(
                        "SCN-CAMERA-TARGET-001",
                        rigObject.name,
                        "FirstPersonCameraControllerのPlayerCameraTargetをPlayer/CameraTargetへ接続してください。",
                        "CinemachineのFollow/LookAt対象がKnightのCameraTargetではありません。");
                }
                else
                {
                    report.AddCheck();
                }
            }

            if (rig != null)
            {
                object lens = GetMemberValue(rig, "Lens");
                object nearClipObj = GetMemberValue(lens, "NearClipPlane");
                if (nearClipObj is float nearClip && nearClip <= 0.04f && nearClip > 0f)
                {
                    report.AddCheck();
                }
                else
                {
                    report.AddError(
                        "SCN-CAMERA-FP-NEARCLIP-001",
                        rigObject.name,
                        "CM_FirstPersonのLens NearClipPlaneを0.04m以下に設定してください。",
                        $"CM_FirstPersonのNearClipPlaneが{nearClipObj}mです。近接穿孔を防ぐため0.04m以下にする必要があります。");
                }
            }

            if (hardLock != null)
            {
                object dampingObj = GetMemberValue(hardLock, "Damping");
                if (dampingObj is float damping && damping > 0f && damping <= 0.1f)
                {
                    report.AddCheck();
                }
                else
                {
                    report.AddError(
                        "SCN-CAMERA-FP-DAMPING-001",
                        rigObject.name,
                        "CM_FirstPersonのCinemachineHardLockToTarget Dampingを0.01〜0.10（推奨0.04）に設定してください。",
                        $"CM_FirstPersonのHardLock Dampingが{dampingObj}です。物理衝突微振動を吸収するため微小減衰が必要です。");
                }
            }

            GameObject mainCameraObject = FindGameObjectByPath(scene, "Camera/MainCamera");
            Camera mainCamera = mainCameraObject != null ? mainCameraObject.GetComponent<Camera>() : null;
            Component brain = mainCameraObject != null ? mainCameraObject.GetComponent("CinemachineBrain") : null;
            if (mainCamera == null || !mainCamera.enabled || brain == null)
            {
                report.AddError(
                    "SCN-CAMERA-BRAIN-001",
                    "Camera/MainCamera",
                    "MainCameraを有効化し、CinemachineBrainを追加してください。",
                    "活動中のMainCameraまたはCinemachineBrainがありません。");
            }
            else
            {
                report.AddCheck();
            }

            if (registry != null && registry.CameraRig != rigObject)
            {
                report.AddError(
                    "SCN-REF-CAMERA-001",
                    registry.name,
                    "SceneReferenceRegistry.CameraRigへCamera/CM_FirstPersonを接続してください。",
                    "SceneReferenceRegistryのCameraRig参照が正式なCinemachineリグと一致しません。");
            }
            else if (registry != null)
            {
                report.AddCheck();
            }

            if (player != null)
            {
                PlayerFirstPersonMeshHandler meshHandler = player.GetComponent<PlayerFirstPersonMeshHandler>();
                if (meshHandler != null)
                {
                    report.AddCheck();

                    bool hasBody = false;
                    bool hasCape = false;
                    var culled = meshHandler.CulledRenderers;
                    for (int i = 0; i < culled.Count; i++)
                    {
                        Renderer r = culled[i];
                        if (r == null) continue;
                        if (r.name.Equals("Knight_Body", StringComparison.OrdinalIgnoreCase)) hasBody = true;
                        if (r.name.Equals("Knight_Cape", StringComparison.OrdinalIgnoreCase)) hasCape = true;
                    }

                    if (hasBody && hasCape)
                    {
                        report.AddCheck();
                    }
                    else
                    {
                        report.AddError(
                            "SCN-CAMERA-FP-MESH-BODY-001",
                            player.gameObject.name,
                            "PlayerFirstPersonMeshHandlerへKnight_BodyおよびKnight_Capeを含めてください。",
                            "第一人称で低頭・走行時に胸甲やマントが近クリップ面を突き破るのを防ぐため、ShadowsOnly対象に指定する必要があります。");
                    }
                }
                else
                {
                    report.AddError(
                        "SCN-CAMERA-FP-MESH-HANDLER-001",
                        player.gameObject.name,
                        "KnightへPlayerFirstPersonMeshHandlerを追加してください。",
                        "第一人称視点での頭部・頭盔・鎧・マントのメッシュ遮蔽管理コンポーネントがありません。");
                }
            }
        }

        private void ValidateNumbers(ValidationReport report, CombatantMarker player, List<CombatantMarker> enemies)
        {
            if (player == null)
            {
                return;
            }

            HealthComponent playerHealth = player.GetComponent<HealthComponent>();
            ValidatePositive(report, "SCN-NUM-PLAYER-HEALTH", player.gameObject.name, playerHealth, "maximumHealth", "Knightの最大体力を0より大きい有限値に設定してください。");
            PlayerController playerController = player.GetComponent<PlayerController>();
            if (playerController != null && IsFinitePositive(playerController.MoveSpeed))
            {
                report.AddCheck();
            }
            else
            {
                report.AddError(
                    "SCN-NUM-PLAYER-MOVE",
                    player.gameObject.name,
                    "PlayerControllerのmoveSpeedを0より大きい有限値に設定してください。",
                    "Knightの移動速度が正の値ではありません。");
            }

            PlayerCombatController playerCombat = player.GetComponent<PlayerCombatController>();
            ValidatePositive(report, "SCN-NUM-PLAYER-ATTACK-RANGE", player.gameObject.name, playerCombat, "attackRange", "Knightの攻撃範囲を0より大きい有限値に設定してください。");
            ValidatePositive(report, "SCN-NUM-PLAYER-ATTACK-DAMAGE", player.gameObject.name, playerCombat, "attackDamage", "Knightの攻撃ダメージを0より大きい有限値に設定してください。");

            for (int index = 0; index < enemies.Count; index++)
            {
                CombatantMarker enemy = enemies[index];
                string prefix = "SCN-NUM-ENEMY-" + (index + 1).ToString("00") + "-";
                ValidatePositive(report, prefix + "HEALTH", enemy.gameObject.name, enemy.GetComponent<HealthComponent>(), "maximumHealth", "敵の最大体力を0より大きい有限値に設定してください。");
                ValidatePositive(report, prefix + "MELEE-RANGE", enemy.gameObject.name, enemy.GetComponent<EnemyBrain>(), "meleeRange", "敵の停止・攻撃距離を0より大きい有限値に設定してください。");
                ValidateNonNegative(report, prefix + "STOPPING-DISTANCE", enemy.gameObject.name, enemy.GetComponent<EnemyBrain>(), "configuredStoppingDistance", "敵のNavMesh停止距離を0以上の有限値に設定してください。");
                ValidatePositive(report, prefix + "ATTACK-RANGE", enemy.gameObject.name, enemy.GetComponent<EnemyMeleeCombat>(), "attackRange", "敵の攻撃範囲を0より大きい有限値に設定してください。");
                ValidatePositive(report, prefix + "ATTACK-DAMAGE", enemy.gameObject.name, enemy.GetComponent<EnemyMeleeCombat>(), "attackDamage", "敵の攻撃ダメージを0より大きい有限値に設定してください。");
                NavMeshAgent agent = enemy.GetComponent<NavMeshAgent>();
                if (agent != null && IsFinitePositive(agent.speed) && IsFinitePositive(agent.radius) && IsFinitePositive(agent.height) && IsFiniteNonNegative(agent.stoppingDistance))
                {
                    report.AddCheck();
                }
                else
                {
                    report.AddError(
                        prefix + "NAV-VALUES",
                        enemy.gameObject.name,
                        "敵NavMeshAgentのspeed、radius、heightを正にし、stoppingDistanceを0以上に設定してください。",
                        $"敵「{enemy.gameObject.name}」のNavMeshAgent数値が不正です。");
                }
            }
        }

        private void ValidateNavigation(ValidationReport report, Scene scene, CombatantMarker player, List<CombatantMarker> enemies)
        {
            GameObject navigation = FindGameObjectByPath(scene, "Navigation");
            Component surface = navigation != null ? navigation.GetComponent("NavMeshSurface") : null;
            if (navigation == null || surface == null)
            {
                report.AddError(
                    "SCN-NAV-SURFACE-001",
                    "Navigation",
                    "NavigationへAI NavigationのNavMeshSurfaceを追加し、必要な地面を収集対象にしてください。",
                    "NavMeshSurfaceが見つかりません。");
            }
            else
            {
                report.AddCheck();
            }

            UnityEngine.AI.NavMeshTriangulation triangulation = UnityEngine.AI.NavMesh.CalculateTriangulation();
            if (triangulation.vertices == null || triangulation.vertices.Length == 0)
            {
                report.AddError(
                    "SCN-NAV-DATA-001",
                    "Navigation",
                    "NavMeshSurfaceをBakeし、プレイ可能領域へNavMeshデータを生成してください。",
                    "NavMeshデータが空です。");
                return;
            }

            report.AddCheck();
            Collider floor = FindGameObjectByPath(scene, "Environment/PlayableFloor")?.GetComponent<Collider>();
            if (floor == null)
            {
                report.AddError(
                    "SCN-NAV-FLOOR-001",
                    "PlayableFloor",
                    "Environment/PlayableFloorへColliderを配置し、プレイ可能領域を定義してください。",
                    "プレイ可能領域の床Colliderが見つかりません。");
            }

            List<Transform> starts = new List<Transform>();
            starts.Add(FindTransformByPath(scene, "Navigation/RouteMarkers/KnightStart"));
            starts.Add(FindTransformByPath(scene, "Navigation/RouteMarkers/EnemyStart_01"));
            starts.Add(FindTransformByPath(scene, "Navigation/RouteMarkers/EnemyStart_02"));
            starts.Add(FindTransformByPath(scene, "Navigation/RouteMarkers/EnemyStart_03"));
            Vector3[] sampledPositions = new Vector3[starts.Count];
            bool[] sampled = new bool[starts.Count];
            for (int index = 0; index < starts.Count; index++)
            {
                Transform start = starts[index];
                string targetName = start == null ? "RouteMarkers" : start.name;
                if (start == null)
                {
                    report.AddError(
                        "SCN-NAV-START-" + (index + 1).ToString("00"),
                        targetName,
                        "Navigation/RouteMarkersへKnightStartとEnemyStart_01〜03を配置してください。",
                        $"NavMesh起点「{targetName}」が見つかりません。");
                    continue;
                }

                if (floor != null && !IsInsideHorizontalBounds(floor.bounds, start.position, PlayableAreaMargin))
                {
                    report.AddError(
                        "SCN-NAV-AREA-" + (index + 1).ToString("00"),
                        start.name,
                        "起点をPlayableFloorのプレイ可能領域内へ移動してください。",
                        $"起点「{start.name}」がプレイ可能領域の外にあります。");
                }
                else
                {
                    report.AddCheck();
                }

                if (!NavMesh.SamplePosition(start.position, out NavMeshHit hit, NavMeshSampleDistance, NavMesh.AllAreas))
                {
                    report.AddError(
                        "SCN-NAV-SAMPLE-" + (index + 1).ToString("00"),
                        start.name,
                        "起点をNavMesh上へ移動するか、NavMeshSurfaceをBakeし直してください。",
                        $"起点「{start.name}」がNavMesh上にありません。");
                    continue;
                }

                sampled[index] = true;
                sampledPositions[index] = hit.position;
                report.AddCheck();
            }

            if (sampled[0])
            {
                for (int index = 1; index < sampled.Length; index++)
                {
                    if (!sampled[index])
                    {
                        continue;
                    }

                    NavMeshPath path = new NavMeshPath();
                    bool pathFound = NavMesh.CalculatePath(sampledPositions[0], sampledPositions[index], NavMesh.AllAreas, path);
                    if (!pathFound || path.status != NavMeshPathStatus.PathComplete)
                    {
                        report.AddError(
                            "SCN-NAV-PATH-" + index.ToString("00"),
                            starts[index].name,
                            "KnightStartから各EnemyStartまで連続したNavMesh経路を作成してください。",
                            $"KnightStartから「{starts[index].name}」への有効なNavMesh経路がありません。経路状態「{path.status}」。");
                    }
                    else
                    {
                        report.AddCheck();
                    }
                }
            }
        }

        private void ValidateCollisionMatrix(ValidationReport report, CombatantMarker player, List<CombatantMarker> enemies)
        {
            if (player == null)
            {
                return;
            }

            if (!Physics.queriesHitTriggers)
            {
                report.AddError(
                    "SCN-PHYS-TRIGGER-001",
                    player.gameObject.name,
                    "Physics.queriesHitTriggersを有効にし、剣と敵のTrigger Colliderを検出可能にしてください。",
                    "PhysicsのTrigger問い合わせが無効です。");
            }
            else
            {
                report.AddCheck();
            }

            for (int index = 0; index < enemies.Count; index++)
            {
                CombatantMarker enemy = enemies[index];
                if (Physics.GetIgnoreLayerCollision(player.gameObject.layer, enemy.gameObject.layer))
                {
                    report.AddError(
                        "SCN-PHYS-MATRIX-" + (index + 1).ToString("00"),
                        enemy.gameObject.name,
                        "Playerと敵のレイヤー衝突をPhysics設定で有効にしてください。",
                        $"Playerレイヤーと敵「{enemy.gameObject.name}」の衝突行列が無視設定です。");
                }
                else
                {
                    report.AddCheck();
                }
            }
        }

        private void ValidateSceneReferences(
            ValidationReport report,
            SceneReferenceRegistry registry,
            GameFlowController flow,
            GameplayClock clock,
            DamageService damageService,
            GameObject hudRoot,
            CombatantMarker player,
            List<CombatantMarker> enemies)
        {
            if (flow != null && flow.SceneReferences != null && registry != null && flow.SceneReferences != registry)
            {
                report.AddError(
                    "SCN-REF-FLOW-REGISTRY-001",
                    flow.name,
                    "GameFlowController.SceneReferenceRegistryへGameRootのRegistryを接続してください。",
                    "GameFlowControllerとSceneReferenceRegistryの参照が一致しません。");
            }
            else if (flow != null && registry != null)
            {
                report.AddCheck();
            }

            if (flow != null && flow.Clock != null && clock != null && flow.Clock != clock)
            {
                report.AddError(
                    "SCN-REF-FLOW-CLOCK-001",
                    flow.name,
                    "GameFlowController.GameplayClockへGameRootのGameplayClockを接続してください。",
                    "GameFlowControllerとGameplayClockの参照が一致しません。");
            }
            else if (flow != null && clock != null)
            {
                report.AddCheck();
            }

            if (flow != null && flow.DamageService != null && damageService != null && flow.DamageService != damageService)
            {
                report.AddError(
                    "SCN-REF-FLOW-DAMAGE-001",
                    flow.name,
                    "GameFlowController.DamageServiceへGameRootのDamageServiceを接続してください。",
                    "GameFlowControllerとDamageServiceの参照が一致しません。");
            }
            else if (flow != null && damageService != null)
            {
                report.AddCheck();
            }

            if (player != null && player.GetComponent<PlayerCombatController>() != null &&
                !player.GetComponent<PlayerCombatController>().ValidateRequiredReferences(out IReadOnlyList<string> diagnostics))
            {
                for (int index = 0; index < diagnostics.Count; index++)
                {
                    report.AddError(
                        "SCN-REF-PLAYER-COMBAT-" + index.ToString("00"),
                        player.gameObject.name,
                        "PlayerCombatControllerのInput、Animator、DamageService、SwordHitbox参照を接続してください。",
                        diagnostics[index]);
                }
            }
            else if (player != null)
            {
                report.AddCheck();
            }

            for (int index = 0; index < enemies.Count; index++)
            {
                EnemyMeleeCombat combat = enemies[index].GetComponent<EnemyMeleeCombat>();
                if (combat == null)
                {
                    continue;
                }

                if (combat.CurrentAttackSequence == null && !Application.isPlaying)
                {
                    // Edit ModeではCombatantMarkerのAwake前に系列が未生成でも、構成項目を別途検査します。
                    report.AddCheck();
                }
            }
        }

        private void ValidatePackage(ValidationReport report, string stableId, string packageName, string requiredVersion, string repairSuggestion)
        {
#if UNITY_EDITOR
            UnityEditor.PackageManager.PackageInfo packageInfo = UnityEditor.PackageManager.PackageInfo.FindForPackageName(packageName);
            if (packageInfo == null)
            {
                report.AddError(stableId, packageName, repairSuggestion, $"{packageName}パッケージが見つかりません。");
                return;
            }

            if (!string.Equals(packageInfo.version, requiredVersion, StringComparison.Ordinal))
            {
                report.AddError(stableId, packageName, repairSuggestion, $"{packageName}のバージョンが一致しません。必要「{requiredVersion}」、検出「{packageInfo.version}」。");
                return;
            }

            if (string.IsNullOrWhiteSpace(packageInfo.resolvedPath))
            {
                report.AddError(stableId, packageName, repairSuggestion, $"{packageName}の解決済みパスがありません。Package Managerの解決状態を確認してください。");
                return;
            }

            report.AddCheck();
#else
            Type packageType = packageName == "com.unity.inputsystem"
                ? Type.GetType("UnityEngine.InputSystem.InputAction, Unity.InputSystem")
                : Type.GetType("Unity.AI.Navigation.NavMeshSurface, Unity.AI.Navigation");
            if (packageType == null)
            {
                report.AddError(stableId, packageName, repairSuggestion, $"{packageName}の実行時型を解決できません。");
            }
            else
            {
                report.AddCheck();
            }
#endif
        }

        private static void AddReferenceResult(ValidationReport report, string stableId, string targetName, UnityEngine.Object value, string repairSuggestion)
        {
            if (value == null)
            {
                report.AddError(stableId, targetName, repairSuggestion, $"対象「{targetName}」の必須参照がありません。");
            }
            else
            {
                report.AddCheck();
            }
        }

        private static void AddComponentResult(ValidationReport report, string stableId, GameObject target, UnityEngine.Object component, string repairSuggestion)
        {
            string targetName = target == null ? "不明" : target.name;
            if (component == null)
            {
                report.AddError(stableId, targetName, repairSuggestion, $"対象「{targetName}」に必須コンポーネントがありません。");
            }
            else
            {
                report.AddCheck();
            }
        }

        private void ValidatePositive(ValidationReport report, string stableId, string targetName, Component component, string fieldName, string repairSuggestion)
        {
            if (component == null || !TryGetFloat(component, fieldName, out float value) || !IsFinitePositive(value))
            {
                report.AddError(stableId, targetName, repairSuggestion, $"対象「{targetName}」の「{fieldName}」が0より大きい有限値ではありません。");
            }
            else
            {
                report.AddCheck();
            }
        }

        private void ValidateNonNegative(ValidationReport report, string stableId, string targetName, Component component, string fieldName, string repairSuggestion)
        {
            if (component == null || !TryGetFloat(component, fieldName, out float value) || !IsFiniteNonNegative(value))
            {
                report.AddError(stableId, targetName, repairSuggestion, $"対象「{targetName}」の「{fieldName}」が0以上の有限値ではありません。");
            }
            else
            {
                report.AddCheck();
            }
        }

        private bool TryGetFloat(Component component, string fieldName, out float value)
        {
            value = 0f;
            if (component == null || string.IsNullOrEmpty(fieldName))
            {
                return false;
            }

            FieldInfo field = component.GetType().GetField(fieldName, instanceFieldFlags);
            if (field == null || field.FieldType != typeof(float))
            {
                return false;
            }

            value = (float)field.GetValue(component);
            return true;
        }

        private static bool IsFinitePositive(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
        }

        private static bool IsFiniteNonNegative(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
        }

        private static object GetFieldValue(object target, string fieldName)
        {
            if (target == null || string.IsNullOrEmpty(fieldName))
            {
                return null;
            }

            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return field?.GetValue(target);
        }

        private static object GetMemberValue(object target, string memberName)
        {
            if (target == null || string.IsNullOrEmpty(memberName))
            {
                return null;
            }

            Type type = target.GetType();
            FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                return field.GetValue(target);
            }

            PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return property != null && property.CanRead ? property.GetValue(target) : null;
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            if (string.Equals(root.name, name, StringComparison.Ordinal))
            {
                return root;
            }

            for (int index = 0; index < root.childCount; index++)
            {
                Transform result = FindDescendant(root.GetChild(index), name);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private static T FindDescendantComponent<T>(Transform root) where T : Component
        {
            if (root == null)
            {
                return null;
            }

            T component = root.GetComponent<T>();
            if (component != null)
            {
                return component;
            }

            for (int index = 0; index < root.childCount; index++)
            {
                component = FindDescendantComponent<T>(root.GetChild(index));
                if (component != null)
                {
                    return component;
                }
            }

            return null;
        }

        private static bool IsInsideHorizontalBounds(Bounds bounds, Vector3 position, float margin)
        {
            return position.x >= bounds.min.x - margin && position.x <= bounds.max.x + margin &&
                position.z >= bounds.min.z - margin && position.z <= bounds.max.z + margin;
        }

        private static T FindComponent<T>(Scene scene) where T : Component
        {
            T[] components = FindComponents<T>(scene);
            return components.Length > 0 ? components[0] : null;
        }

        private static T[] FindComponents<T>(Scene scene) where T : Component
        {
            List<T> result = new List<T>();
            if (!scene.IsValid())
            {
                return result.ToArray();
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int index = 0; index < roots.Length; index++)
            {
                T[] components = roots[index].GetComponentsInChildren<T>(true);
                result.AddRange(components);
            }

            return result.ToArray();
        }

        private static GameObject FindGameObjectByPath(Scene scene, string path)
        {
            Transform transform = FindTransformByPath(scene, path);
            return transform != null ? transform.gameObject : null;
        }

        private static Transform FindTransformByPath(Scene scene, string path)
        {
            if (!scene.IsValid() || string.IsNullOrEmpty(path))
            {
                return null;
            }

            string[] parts = path.Split('/');
            GameObject[] roots = scene.GetRootGameObjects();
            Transform current = null;
            for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                if (string.Equals(roots[rootIndex].name, parts[0], StringComparison.Ordinal))
                {
                    current = roots[rootIndex].transform;
                    break;
                }
            }

            if (current == null)
            {
                return null;
            }

            for (int index = 1; index < parts.Length; index++)
            {
                current = current.Find(parts[index]);
                if (current == null)
                {
                    return null;
                }
            }

            return current;
        }
    }
}
