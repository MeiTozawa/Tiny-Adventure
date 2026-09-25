using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.SceneManagement;
using VContainer;

namespace TinyAdventure
{
    /// <summary>
    /// Demo全体の初期化、進行、終局状態を一元管理します。
    /// GameplayStateを書き換える正式な実装はこのコンポーネントだけです。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameFlowController : MonoBehaviour, IGameplayStateProvider
    {
        [Header("必須参照")]
        [SerializeField]
        private SceneReferenceRegistry sceneReferenceRegistry;

        [SerializeField]
        private GameplayClock gameplayClock;

        [SerializeField]
        private DamageService damageService;
        
        [SerializeField]
        private HitStopController hitStopController;

        [SerializeField]
        private InputReader inputReader;

        [Header("再開設定")]
        [SerializeField]
        private bool reloadSceneOnRestart;

        [SerializeField]
        private string restartSceneName = "SampleScene";

        [Header("終了アダプター")]
        private readonly List<GameFlowInitializationStage> initializationTrace = new();
        private readonly GameplayWinLossTracker winLossTracker = new();
        private readonly GameFlowInputHandler inputHandler = new();
        private bool playerStartedWithoutHealth;

        public GameplayState CurrentState { get; private set; } = GameplayState.Boot;
        public bool IsTerminal => CurrentState == GameplayState.Victory || CurrentState == GameplayState.Defeat;
        public bool IsGameplayInputEnabled => CurrentState == GameplayState.Running;
        public bool IsHudReady { get; private set; }
        public GameFlowInitializationStage InitializationStage { get; private set; } = GameFlowInitializationStage.Boot;
        public IReadOnlyList<GameFlowInitializationStage> InitializationTrace => initializationTrace;
        public SceneReferenceRegistry SceneReferences => sceneReferenceRegistry;
        public GameplayClock Clock => gameplayClock;
        public DamageService DamageService => damageService;
        public bool ReloadSceneOnRestart => reloadSceneOnRestart;
        public string RestartSceneName => restartSceneName;
        public GameplayWinLossTracker WinLossTracker => winLossTracker;
        public GameFlowInputHandler InputHandler => inputHandler;

        public event Action<GameplayState> StateChanged;
        public event Action<GameFlowInitializationStage> InitializationStageChanged;
        public event Action HudPreparationRequested;
        public event Action RestartRequested;
        public event Action ExitRequested;

        [Inject]
        public void Construct(
            ICombatantRegistry registry = null,
            IGameplayClock clock = null,
            IDamageService damage = null,
            IHitStopController hitStop = null)
        {
            if (registry is SceneReferenceRegistry srr) sceneReferenceRegistry = srr;
            if (clock is GameplayClock gc) gameplayClock = gc;
            if (damage is DamageService ds) damageService = ds;
            if (hitStop is HitStopController hsc) hitStopController = hsc;
        }

        public void Construct(
            SceneReferenceRegistry registry,
            GameplayClock clock,
            DamageService damage,
            InputReader input)
        {
            Construct(registry, clock, damage);
            if (input != null) inputReader = input;
        }

        private void Awake()
        {
            CurrentState = GameplayState.Boot;
            InitializationStage = GameFlowInitializationStage.Boot;
            winLossTracker.DefeatConditionMet += () => RequestDefeat();
            winLossTracker.VictoryConditionMet += () =>
            {
                if (CurrentState == GameplayState.Running)
                {
                    RequestVictory();
                }
            };
        }

        internal void Start()
        {
            CurrentState = GameplayState.Boot;
            playerStartedWithoutHealth = false;
            initializationTrace.Clear();
            SetInitializationStage(GameFlowInitializationStage.Boot);

            SetInitializationStage(GameFlowInitializationStage.Validation);
            Assert.IsNotNull(sceneReferenceRegistry, "GameFlowController: SceneReferenceRegistryコンポーネントが未設定です。");
            Assert.IsNotNull(damageService, "GameFlowController: DamageServiceコンポーネントが未設定です。");
            Assert.IsNotNull(sceneReferenceRegistry.Player, "GameFlowController: Player参照が未設定です。");
            Assert.IsNotNull(sceneReferenceRegistry.Player.Health, "GameFlowController: PlayerのHealthComponentが未設定です。");
            Assert.IsTrue(sceneReferenceRegistry.Player.Health.MaximumHealth > 0f && !float.IsInfinity(sceneReferenceRegistry.Player.Health.MaximumHealth), "GameFlowController: Playerの最大体力が不正です。");

            if (inputReader == null && sceneReferenceRegistry.Player != null)
            {
                inputReader = sceneReferenceRegistry.Player.GetComponent<InputReader>();
            }

            SetInitializationStage(GameFlowInitializationStage.Registration);
            sceneReferenceRegistry.ClearRuntimeRegistrations();
            damageService.ConfigureForRuntime(this, gameplayClock, sceneReferenceRegistry);
            gameplayClock?.ConfigureStateProvider(this);
            RegisterCombatants();

            SetInitializationStage(GameFlowInitializationStage.SpawnSnapshot);
            sceneReferenceRegistry.CaptureSpawnSnapshot();

            SetInitializationStage(GameFlowInitializationStage.HealthAndEnemyInitialization);
            InitializeCombatants();

            SubscribeToHealthComponents();
            SetInitializationStage(GameFlowInitializationStage.HudPreparation);
            try
            {
                HudPreparationRequested?.Invoke();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }

            IsHudReady = sceneReferenceRegistry.PrepareHud(this);
            Assert.IsTrue(IsHudReady, "GameFlowController: HUDの準備に失敗しました。");

            if (playerStartedWithoutHealth)
            {
                TransitionTo(GameplayState.Defeat);
            }
            else if (sceneReferenceRegistry.ActiveEnemyCount == 0)
            {
                TransitionTo(GameplayState.Victory);
            }
            else
            {
                TransitionTo(GameplayState.Running);
            }
        }

        private void Update()
        {
            if (CurrentState == GameplayState.Boot)
            {
                return;
            }

            var action = inputHandler.EvaluateFrameInput(inputReader, IsTerminal);
            if (action == GameFlowInputHandler.FlowAction.Restart)
            {
                RequestRestart();
            }
            else if (action == GameFlowInputHandler.FlowAction.Exit)
            {
                RequestExit();
            }
        }

        /// <summary>入力スナップショットをGameFlowの再開・終了入口へ渡します。</summary>
        public void ProcessInput(GameplayInputSnapshot snapshot)
        {
            var action = inputHandler.EvaluateInput(snapshot, IsTerminal);
            if (action == GameFlowInputHandler.FlowAction.Restart)
            {
                RequestRestart();
            }
            else if (action == GameFlowInputHandler.FlowAction.Exit)
            {
                RequestExit();
            }
        }

        private void OnDestroy()
        {
            UnsubscribeFromHealthComponents();
        }

        /// <summary>
        /// 公開された状態書き換え入口です。終局状態から別の終局状態へは遷移できません。
        /// </summary>
        public Result SetState(GameplayState nextState)
        {
            if (nextState == CurrentState)
            {
                return GameError.InvalidState;
            }

            if (CurrentState == GameplayState.Victory || CurrentState == GameplayState.Defeat)
            {
                if (nextState != GameplayState.Restarting)
                {
                    return GameError.StateAlreadyTerminal;
                }
            }

            if (CurrentState == GameplayState.Restarting)
            {
                return GameError.InvalidState;
            }

            if (nextState == GameplayState.Boot && CurrentState != GameplayState.Boot)
            {
                return GameError.StateTransitionRejected;
            }

            TransitionTo(nextState);
            return Result.Ok();
        }

        /// <summary>敵集合が空になったときにVictoryを要求します。</summary>
        public Result RequestVictory()
        {
            if (CurrentState != GameplayState.Running)
            {
                return GameError.InvalidState;
            }

            TransitionTo(GameplayState.Victory);
            return Result.Ok();
        }

        /// <summary>Player死亡時にDefeatを要求します。</summary>
        public Result RequestDefeat()
        {
            if (CurrentState != GameplayState.Running)
            {
                return GameError.InvalidState;
            }

            TransitionTo(GameplayState.Defeat);
            return Result.Ok();
        }

        /// <summary>終局中だけRestartingへ遷移し、後続のシーン再読み込みを通知します。</summary>
        public Result RequestRestart()
        {
            if (!IsTerminal)
            {
                return GameError.RestartNotAllowed;
            }

            GameplayState previousState = CurrentState;
            Result setResult = SetState(GameplayState.Restarting);
            if (setResult.IsErr)
            {
                return setResult.Error;
            }

            try
            {
                RestartRequested?.Invoke();
                if (reloadSceneOnRestart)
                {
                    string sceneName = string.IsNullOrWhiteSpace(restartSceneName)
                        ? SceneManager.GetActiveScene().name
                        : restartSceneName;
                    SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
                }

                return Result.Ok();
            }
            catch (Exception)
            {
                if (CurrentState == GameplayState.Restarting)
                {
                    TransitionTo(previousState);
                }
                throw;
            }
        }

        /// <summary>プラットフォーム終了処理を実行します。</summary>
        public void RequestExit()
        {
            ExitRequested?.Invoke();
            GameAppUtils.Quit();
        }

        private void TransitionTo(GameplayState nextState)
        {
            if (CurrentState == nextState)
            {
                return;
            }

            CurrentState = nextState;
            if (nextState == GameplayState.Running)
            {
                SetInitializationStage(GameFlowInitializationStage.Running);
                gameplayClock?.ResumeGameplay();
            }
            else if (nextState == GameplayState.Victory)
            {
                SetInitializationStage(GameFlowInitializationStage.Victory);
                gameplayClock?.PauseGameplay();
            }
            else if (nextState == GameplayState.Defeat)
            {
                SetInitializationStage(GameFlowInitializationStage.Defeat);
                gameplayClock?.PauseGameplay();
            }
            else if (nextState == GameplayState.Restarting)
            {
                SetInitializationStage(GameFlowInitializationStage.Restarting);
                gameplayClock?.PauseGameplay();
            }

            StateChanged?.Invoke(nextState);
        }

        public void Construct(SceneReferenceRegistry sceneRegistry = null)
        {
            if (sceneRegistry != null) sceneReferenceRegistry = sceneRegistry;
        }

        private void RegisterCombatants()
        {
            sceneReferenceRegistry.Register(sceneReferenceRegistry.Player);

            IReadOnlyList<CombatantMarker> enemies = sceneReferenceRegistry.ConfiguredEnemies;
            for (int index = 0; index < enemies.Count; index++)
            {
                CombatantMarker enemy = enemies[index];
                if (enemy == null || !enemy.gameObject.activeInHierarchy)
                {
                    continue;
                }

                sceneReferenceRegistry.Register(enemy);
            }
        }

        private void InitializeCombatants()
        {
            CombatantMarker player = sceneReferenceRegistry.Player;
            HealthComponent playerHealth = player.Health;
            Assert.IsNotNull(playerHealth, "GameFlowController: PlayerのHealthComponentが未設定です。");
            playerStartedWithoutHealth = playerHealth.CurrentHealth <= 0f || !playerHealth.IsAlive;
            if (!playerStartedWithoutHealth)
            {
                playerHealth.EnterDemo();
            }

            IReadOnlyList<CombatantMarker> enemies = sceneReferenceRegistry.ConfiguredEnemies;
            foreach (var enemy in enemies)
            {
                if (enemy == null || !enemy.gameObject.activeInHierarchy)
                {
                    continue;
                }

                HealthComponent enemyHealth = enemy.Health;
                Assert.IsNotNull(enemyHealth, $"GameFlowController: 敵「{enemy.gameObject.name}」のHealthComponentが未設定です。");
                enemyHealth.EnterDemo();

                // 敵の全コンポーネントへ依存性を確実に供給
                EnemyController controller = enemy.GetComponent<EnemyController>();
                if (controller != null)
                {
                    controller.Construct(this, gameplayClock, damageService, sceneReferenceRegistry);
                }

                HitStopParticipant hitStop = enemy.GetComponent<HitStopParticipant>();
                if (hitStop != null && hitStopController != null)
                {
                    hitStop.Construct(hitStopController);
                }
            }
        }

        private void SubscribeToHealthComponents()
        {
            winLossTracker.SubscribeToCombatants(sceneReferenceRegistry);
        }

        private void UnsubscribeFromHealthComponents()
        {
            winLossTracker.Unsubscribe();
        }

        private void SetInitializationStage(GameFlowInitializationStage stage)
        {
            InitializationStage = stage;
            initializationTrace.Add(stage);
            InitializationStageChanged?.Invoke(stage);
        }
    }

    /// <summary>GameFlowが実行した初期化段階です。</summary>
    public enum GameFlowInitializationStage
    {
        Boot,
        Validation,
        Registration,
        SpawnSnapshot,
        HealthAndEnemyInitialization,
        HudPreparation,
        Running,
        Victory,
        Defeat,
        Restarting,
        Failed
    }
}
