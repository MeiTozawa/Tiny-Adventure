using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
using Unity.Cinemachine;

namespace TinyAdventure
{
    /// <summary>
    /// シーン内の戦闘対象と実行時依存を一度だけ解決し、以後はキャッシュして提供します。
    /// シーンレベルのサービスコンテキスト（Service Context）として機能します。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SceneReferenceRegistry : MonoBehaviour, ICombatantRegistry
    {
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
        private bool referencesResolved;
        private bool snapshotCaptured;

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

        public event Action<CombatantMarker> CombatantRegistered;
        public event Action<CombatantMarker> CombatantUnregistered;
        public event Action<int> ActiveEnemyCountChanged;

        private void Awake()
        {
            ResolveSceneReferences();
        }

        public bool ResolveSceneReferences()
        {
            if (referencesResolved && IsConfigurationValid)
            {
                return true;
            }

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
                damageService = GetComponent<DamageService>();
            }

            if (gameFlowController == null)
            {
                gameFlowController = GetComponent<GameFlowController>();
            }

            if (gameplayClock == null)
            {
                gameplayClock = GetComponent<GameplayClock>();
            }

            if (inputReader == null)
            {
                inputReader = GetComponent<InputReader>();
            }

            if (hudRoot == null)
            {
                DemoHudController hud = FindAnyObjectByType<DemoHudController>();
                hudRoot = hud != null ? hud.gameObject : null;
            }

            if (cameraRig == null)
            {
                CinemachineCamera vcam = FindAnyObjectByType<CinemachineCamera>();
                cameraRig = vcam != null ? vcam.gameObject : null;
            }

            referencesResolved = IsConfigurationValid;
            return referencesResolved;
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

        /// <summary>指定された戦闘対象を登録します。無効な引数はアサーションで中断します。</summary>
        public void Register(CombatantMarker combatant)
        {
            Assert.IsNotNull(combatant, "SceneReferenceRegistry: 登録する戦闘対象が未設定です。");

            if (!combatant.IsAvailableForCombat)
            {
                return;
            }

            if (!registeredCombatants.Add(combatant))
            {
                return;
            }

            if (combatant.Faction == CombatantMarker.CombatantFaction.Enemy)
            {
                activeEnemies.Add(combatant);
                ActiveEnemyCountChanged?.Invoke(activeEnemies.Count);
            }

            CombatantRegistered?.Invoke(combatant);
        }

        /// <summary>戦闘対象を登録から解除し、敵集合の変更を通知します。</summary>
        public void Unregister(CombatantMarker combatant)
        {
            Assert.IsNotNull(combatant, "SceneReferenceRegistry: 解除する戦闘対象が未設定です。");

            if (!registeredCombatants.Remove(combatant))
            {
                return;
            }

            bool removedEnemy = activeEnemies.Remove(combatant);
            CombatantUnregistered?.Invoke(combatant);
            if (removedEnemy)
            {
                ActiveEnemyCountChanged?.Invoke(activeEnemies.Count);
            }
        }

        public bool IsRegistered(CombatantMarker combatant)
        {
            Assert.IsNotNull(combatant, "SceneReferenceRegistry: 確認する戦闘対象が未設定です。");
            return registeredCombatants.Contains(combatant);
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
        /// </summary>
        public void CaptureSpawnSnapshot()
        {
            ResolveSceneReferences();
            spawnSnapshots.Clear();
            snapshotCaptured = false;

            CaptureCombatantSnapshot(player, true);

            for (int index = 0; index < configuredEnemies.Count; index++)
            {
                CombatantMarker enemy = configuredEnemies[index];
                if (enemy == null || !enemy.gameObject.activeInHierarchy)
                {
                    continue;
                }

                CaptureCombatantSnapshot(enemy, false);
            }

            snapshotCaptured = true;
        }

        /// <summary>保存済みスナップショットへPlayerと初期敵を復元します。</summary>
        public Result RestoreSpawnSnapshot()
        {
            if (!snapshotCaptured)
            {
                return GameError.InvalidState;
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
                HealthComponent health = marker.Health;
                if (health != null)
                {
                    health.EnterDemo();
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

            return Result.Ok();
        }

        /// <summary>HUD実装が存在する場合だけ準備を依頼します。</summary>
        public bool PrepareHud(GameFlowController flow)
        {
            IGameplayHudPreparation preparation = hudPreparationComponent as IGameplayHudPreparation;
            if (preparation == null)
            {
                return true;
            }

            return preparation.Prepare(flow, this, out _);
        }

        private void CaptureCombatantSnapshot(CombatantMarker marker, bool isPlayer)
        {
            Assert.IsNotNull(marker, isPlayer ? "SceneReferenceRegistry: Player参照が空のためSpawnSnapshotを保存できません。" : "SceneReferenceRegistry: 敵参照が空のためSpawnSnapshotを保存できません。");
            HealthComponent health = marker.Health;
            Assert.IsNotNull(health, $"SceneReferenceRegistry: 戦闘対象「{marker.gameObject.name}」のHealthComponentが未設定です。");
            Assert.IsTrue(health.MaximumHealth > 0f && !float.IsInfinity(health.MaximumHealth), $"SceneReferenceRegistry: 戦闘対象「{marker.gameObject.name}」の初期体力設定が不正です。");

            float restoreHealth = health.MaximumHealth;
            Result<SpawnSnapshot> snapshotResult = SpawnSnapshot.Create(marker.transform.position, marker.transform.rotation, restoreHealth);
            Assert.IsTrue(snapshotResult.IsOk, $"SceneReferenceRegistry: 戦闘対象「{marker.gameObject.name}」のSpawnSnapshot保存に失敗しました。");

            spawnSnapshots[marker] = snapshotResult.Value;
        }
    }

    /// <summary>Task 7.2のHUDが初期値準備を提供するための任意インターフェースです。</summary>
    public interface IGameplayHudPreparation
    {
        bool Prepare(GameFlowController flow, SceneReferenceRegistry registry, out string diagnostic);
    }
}
