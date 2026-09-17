using System;
using System.Collections.Generic;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// シーン内の戦闘対象と実行時依存を一度だけ解決し、以後はキャッシュして提供します。
    /// シーンレベルのサービスコンテキスト（Service Context）として機能します。
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000)]
    public sealed class SceneReferenceRegistry : MonoBehaviour, ICombatantRegistry
    {
        private static SceneReferenceRegistry activeInstance;

        public static SceneReferenceRegistry ActiveInstance
        {
            get
            {
                if (activeInstance == null)
                {
                    activeInstance = FindAnyObjectByType<SceneReferenceRegistry>();
                }
                return activeInstance;
            }
            internal set => activeInstance = value;
        }

        [Header("必須シーン参照")]
        [SerializeField]
        private CombatantMarker player;

        [SerializeField]
        private List<CombatantMarker> configuredEnemies = new List<CombatantMarker>();

        [SerializeField]
        private DamageService damageService;

        [SerializeField]
        private GameFlowController gameFlowController;

        [SerializeField]
        private GameplayClock gameplayClock;

        [SerializeField]
        private InputReader inputReader;

        [Tooltip("Task 7.2でHUD実装を接続するための任意のルートです。")]
        [SerializeField]
        private GameObject hudRoot;

        [Tooltip("HUD実装が提供する準備インターフェースです。未設定でもTask 7.1の初期化は阻害しません。")]
        [SerializeField]
        private MonoBehaviour hudPreparationComponent;

        [Tooltip("正式な第一人称カメラリグです。")]
        [SerializeField]
        private GameObject cameraRig;

        private readonly HashSet<CombatantMarker> registeredCombatants = new HashSet<CombatantMarker>();
        private readonly HashSet<CombatantMarker> activeEnemies = new HashSet<CombatantMarker>();
        private readonly Dictionary<CombatantMarker, SpawnSnapshot> spawnSnapshots = new Dictionary<CombatantMarker, SpawnSnapshot>();
        private readonly List<string> diagnostics = new List<string>();
        private bool referencesResolved;
        private bool snapshotCaptured;
        private bool diagnosticLogged;

        public CombatantMarker Player => player;
        public DamageService DamageService => damageService;
        public GameFlowController GameFlowController => gameFlowController;
        public GameplayClock GameplayClock => gameplayClock;
        public InputReader InputReader => inputReader;
        public GameObject HudRoot => hudRoot;
        public GameObject CameraRig => cameraRig;
        public bool IsHudPreparationAvailable => hudPreparationComponent is IGameplayHudPreparation;
        public bool IsSnapshotCaptured => snapshotCaptured;
        public int ActiveEnemyCount => activeEnemies.Count;
        public IReadOnlyCollection<CombatantMarker> ActiveEnemies => activeEnemies;
        public IReadOnlyList<CombatantMarker> ConfiguredEnemies => configuredEnemies;
        public IReadOnlyDictionary<CombatantMarker, SpawnSnapshot> SpawnSnapshots => spawnSnapshots;
        public IReadOnlyList<string> Diagnostics => diagnostics;
        public string LastDiagnostic { get; private set; } = string.Empty;

        public event Action<CombatantMarker> CombatantRegistered;
        public event Action<CombatantMarker> CombatantUnregistered;
        public event Action<int> ActiveEnemyCountChanged;
        public event Action<string> DiagnosticReported;

        private void OnEnable()
        {
            activeInstance = this;
        }

        private void OnDisable()
        {
            if (activeInstance == this)
            {
                activeInstance = null;
            }
        }

        private void Awake()
        {
            activeInstance = this;
            ResolveSceneReferences();
        }

        /// <summary>シーン参照を一度だけ解決します。毎フレームの全シーン検索は行いません。</summary>
        public bool ResolveSceneReferences()
        {
            if (referencesResolved)
            {
                return IsConfigurationValid;
            }

            referencesResolved = true;
            if (player == null)
            {
                CombatantMarker[] markers = FindObjectsByType<CombatantMarker>(FindObjectsInactive.Include);
                for (int index = 0; index < markers.Length; index++)
                {
                    CombatantMarker marker = markers[index];
                    if (marker != null && marker.Faction == CombatantMarker.CombatantFaction.Player)
                    {
                        if (player != null)
                        {
                            AddDiagnostic("Player陣営のKnightが複数登録されています。", true);
                            break;
                        }

                        player = marker;
                    }
                }
            }

            if (configuredEnemies == null)
            {
                configuredEnemies = new List<CombatantMarker>();
            }

            if (configuredEnemies.Count == 0)
            {
                CombatantMarker[] markers = FindObjectsByType<CombatantMarker>(FindObjectsInactive.Include);
                for (int index = 0; index < markers.Length; index++)
                {
                    CombatantMarker marker = markers[index];
                    if (marker != null && marker.Faction == CombatantMarker.CombatantFaction.Enemy && !configuredEnemies.Contains(marker))
                    {
                        configuredEnemies.Add(marker);
                    }
                }
            }

            if (damageService == null)
            {
                damageService = GetComponent<DamageService>() ?? FindAnyObjectByType<DamageService>();
            }

            if (gameFlowController == null)
            {
                gameFlowController = GetComponent<GameFlowController>() ?? FindAnyObjectByType<GameFlowController>();
            }

            if (gameplayClock == null)
            {
                gameplayClock = GetComponent<GameplayClock>() ?? FindAnyObjectByType<GameplayClock>();
            }

            if (inputReader == null)
            {
                inputReader = GetComponent<InputReader>() ?? FindAnyObjectByType<InputReader>();
            }

            if (hudRoot == null)
            {
                hudRoot = GameObject.Find("UI/HUDRoot");
            }

            if (cameraRig == null)
            {
                cameraRig = GameObject.Find("Camera/CM_FirstPerson");
            }

            ValidateReferences();
            return IsConfigurationValid;
        }

        /// <summary>Flow初期化で使用する参照の妥当性です。敵0体は有効な境界条件です。</summary>
        public bool IsConfigurationValid
        {
            get
            {
                if (player == null || damageService == null)
                {
                    return false;
                }

                for (int index = 0; index < configuredEnemies.Count; index++)
                {
                    if (configuredEnemies[index] == null)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>指定された戦闘対象を一度だけ登録します。</summary>
        public bool Register(CombatantMarker combatant)
        {
            if (combatant == null)
            {
                return ReportFailure("空の戦闘対象を登録できませんでした。", true);
            }

            if (!combatant.IsIdentityValid)
            {
                return ReportFailure($"戦闘対象「{combatant.gameObject.name}」のIDが無効なため登録できませんでした。", true);
            }

            if (!combatant.IsAvailableForCombat)
            {
                return ReportFailure($"戦闘対象「{combatant.CombatantId}」が失活または破棄済みのため登録できませんでした。", false);
            }

            if (!registeredCombatants.Add(combatant))
            {
                return ReportFailure($"戦闘対象「{combatant.CombatantId}」は重複登録されています。", false);
            }

            if (combatant.Faction == CombatantMarker.CombatantFaction.Enemy)
            {
                activeEnemies.Add(combatant);
                ActiveEnemyCountChanged?.Invoke(activeEnemies.Count);
            }

            CombatantRegistered?.Invoke(combatant);
            return true;
        }

        /// <summary>戦闘対象を登録から解除し、敵集合の変更を通知します。</summary>
        public bool Unregister(CombatantMarker combatant)
        {
            if (combatant == null)
            {
                return ReportFailure("空の戦闘対象を登録解除できませんでした。", false);
            }

            if (!registeredCombatants.Remove(combatant))
            {
                return ReportFailure($"戦闘対象「{combatant.CombatantId}」は登録されていないため解除できませんでした。", false);
            }

            bool removedEnemy = activeEnemies.Remove(combatant);
            CombatantUnregistered?.Invoke(combatant);
            if (removedEnemy)
            {
                ActiveEnemyCountChanged?.Invoke(activeEnemies.Count);
            }

            return true;
        }

        public bool IsRegistered(CombatantMarker combatant)
        {
            return combatant != null && registeredCombatants.Contains(combatant);
        }

        /// <summary>DamageServiceとFlowが共有する登録簿を空にして再登録します。</summary>
        public void ClearRuntimeRegistrations()
        {
            registeredCombatants.Clear();
            activeEnemies.Clear();
            ActiveEnemyCountChanged?.Invoke(0);
        }

        /// <summary>
        /// Playerと初期敵の位置、回転、初期体力を安定したスナップショットへ保存します。
        /// 現在体力0のPlayerは終局判定用に0を観測しつつ、復元値には最大体力を保存します。
        /// </summary>
        public bool CaptureSpawnSnapshot(out string diagnostic)
        {
            ResolveSceneReferences();
            spawnSnapshots.Clear();
            snapshotCaptured = false;

            if (!TryCaptureCombatantSnapshot(player, true, out diagnostic))
            {
                return false;
            }

            for (int index = 0; index < configuredEnemies.Count; index++)
            {
                CombatantMarker enemy = configuredEnemies[index];
                if (enemy == null || !enemy.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (!TryCaptureCombatantSnapshot(enemy, false, out diagnostic))
                {
                    return false;
                }
            }

            snapshotCaptured = true;
            diagnostic = string.Empty;
            return true;
        }

        /// <summary>保存済みスナップショットへPlayerと初期敵を復元します。</summary>
        public bool RestoreSpawnSnapshot(out string diagnostic)
        {
            if (!snapshotCaptured)
            {
                diagnostic = "SpawnSnapshotが保存されていないため復元できません。";
                AddDiagnostic(diagnostic, true);
                return false;
            }

            foreach (KeyValuePair<CombatantMarker, SpawnSnapshot> pair in spawnSnapshots)
            {
                CombatantMarker marker = pair.Key;
                if (marker == null)
                {
                    continue;
                }

                marker.gameObject.SetActive(true);
                marker.transform.SetPositionAndRotation(pair.Value.Position, pair.Value.Rotation);
                HealthComponent health = marker.GetComponent<HealthComponent>();
                if (health != null && !health.EnterDemo(out diagnostic))
                {
                    AddDiagnostic(diagnostic, true);
                    return false;
                }
            }

            ClearRuntimeRegistrations();
            for (int index = 0; index < configuredEnemies.Count; index++)
            {
                CombatantMarker enemy = configuredEnemies[index];
                if (enemy != null && enemy.gameObject.activeInHierarchy)
                {
                    Register(enemy);
                }
            }

            if (player != null && player.gameObject.activeInHierarchy)
            {
                Register(player);
            }

            diagnostic = string.Empty;
            return true;
        }

        /// <summary>HUD実装が存在する場合だけ準備を依頼します。</summary>
        public bool PrepareHud(GameFlowController flow, out string diagnostic)
        {
            IGameplayHudPreparation preparation = hudPreparationComponent as IGameplayHudPreparation;
            if (preparation == null)
            {
                diagnostic = string.Empty;
                return true;
            }

            bool prepared = preparation.Prepare(flow, this, out diagnostic);
            if (!prepared && !string.IsNullOrEmpty(diagnostic))
            {
                AddDiagnostic(diagnostic, true);
            }

            return prepared;
        }

        private bool TryCaptureCombatantSnapshot(CombatantMarker marker, bool isPlayer, out string diagnostic)
        {
            if (marker == null)
            {
                diagnostic = isPlayer ? "Player参照が空のためSpawnSnapshotを保存できません。" : "敵参照が空のためSpawnSnapshotを保存できません。";
                AddDiagnostic(diagnostic, true);
                return false;
            }

            HealthComponent health = marker.GetComponent<HealthComponent>();
            if (health == null || !DamageRequest.IsFinitePositiveAmount(health.MaximumHealth))
            {
                diagnostic = $"戦闘対象「{marker.gameObject.name}」の初期体力設定が不正です。";
                AddDiagnostic(diagnostic, true);
                return false;
            }

            float restoreHealth = health.MaximumHealth;
            if (!SpawnSnapshot.TryCreate(marker.transform.position, marker.transform.rotation, restoreHealth, out SpawnSnapshot snapshot, out diagnostic))
            {
                AddDiagnostic($"戦闘対象「{marker.gameObject.name}」のSpawnSnapshot保存に失敗しました。{diagnostic}", true);
                return false;
            }

            spawnSnapshots[marker] = snapshot;
            return true;
        }

        private void ValidateReferences()
        {
            if (player == null)
            {
                AddDiagnostic("SceneReferenceRegistryにPlayer参照がありません。", true);
            }
            else if (!player.IsIdentityValid)
            {
                AddDiagnostic("Playerの戦闘対象IDが無効です。", true);
            }

            if (damageService == null)
            {
                AddDiagnostic("SceneReferenceRegistryにDamageService参照がありません。", true);
            }

            HashSet<CombatantMarker> seenEnemies = new HashSet<CombatantMarker>();
            for (int index = 0; index < configuredEnemies.Count; index++)
            {
                CombatantMarker enemy = configuredEnemies[index];
                if (enemy == null)
                {
                    AddDiagnostic($"敵参照 #{index + 1} が空です。", true);
                    continue;
                }

                if (!seenEnemies.Add(enemy))
                {
                    AddDiagnostic($"敵「{enemy.gameObject.name}」が重複登録されています。", true);
                }

                if (enemy.Faction != CombatantMarker.CombatantFaction.Enemy)
                {
                    AddDiagnostic($"戦闘対象「{enemy.gameObject.name}」がEnemy陣営ではありません。", true);
                }

                if (!enemy.gameObject.activeInHierarchy)
                {
                    AddDiagnostic($"敵「{enemy.gameObject.name}」が失活しています。初期敵集合から除外されます。", false);
                }
            }
        }

        private bool ReportFailure(string message, bool asError)
        {
            AddDiagnostic(message, asError);
            return false;
        }

        private void AddDiagnostic(string message, bool asError)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            LastDiagnostic = message;
            if (!diagnostics.Contains(message))
            {
                diagnostics.Add(message);
            }

            if (asError && !diagnosticLogged)
            {
                diagnosticLogged = true;
                Debug.LogError($"[シーン参照診断] {message}", this);
            }
            else if (!asError)
            {
                Debug.LogWarning($"[シーン参照診断] {message}", this);
            }

            DiagnosticReported?.Invoke(message);
        }
    }

    /// <summary>Task 7.2のHUDが初期値準備を提供するための任意インターフェースです。</summary>
    public interface IGameplayHudPreparation
    {
        bool Prepare(GameFlowController flow, SceneReferenceRegistry registry, out string diagnostic);
    }
}
