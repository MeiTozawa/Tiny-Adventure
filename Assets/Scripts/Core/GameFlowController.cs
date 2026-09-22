using System;
using System.Collections.Generic;
using UnityEngine;
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
        private InputReader inputReader;

        [Header("再開設定")]
        [SerializeField]
        private bool reloadSceneOnRestart;

        [SerializeField]
        private string restartSceneName = "SampleScene";

        [Header("終了アダプター")]
        [SerializeField]
        private EditorApplicationExit editorApplicationExitAdapter;

        [SerializeField]
        private RuntimeApplicationExit runtimeApplicationExitAdapter;

        private IApplicationExit applicationExitAdapter;
        private readonly List<GameFlowInitializationStage> initializationTrace = new List<GameFlowInitializationStage>();
        private readonly GameplayWinLossTracker winLossTracker = new GameplayWinLossTracker();
        private readonly GameFlowInputHandler inputHandler = new GameFlowInputHandler();
        private bool initialized;
        private bool initializationFailed;
        private bool playerStartedWithoutHealth;

        public GameplayState CurrentState { get; private set; } = GameplayState.Boot;
        public bool IsTerminal => CurrentState == GameplayState.Victory || CurrentState == GameplayState.Defeat;
        public bool IsGameplayInputEnabled => CurrentState == GameplayState.Running;
        public bool IsInitialized => initialized;
        public bool IsInitializationFailed => initializationFailed;
        public bool IsHudReady { get; private set; }
        public GameFlowInitializationStage InitializationStage { get; private set; } = GameFlowInitializationStage.Boot;
        public IReadOnlyList<GameFlowInitializationStage> InitializationTrace => initializationTrace;
        public SceneReferenceRegistry SceneReferences => sceneReferenceRegistry;
        public GameplayClock Clock => gameplayClock;
        public DamageService DamageService => damageService;
        public bool ReloadSceneOnRestart => reloadSceneOnRestart;
        public string RestartSceneName => restartSceneName;
        public IApplicationExit ApplicationExitAdapter => applicationExitAdapter;
        public GameplayWinLossTracker WinLossTracker => winLossTracker;
        public GameFlowInputHandler InputHandler => inputHandler;
        public string LastDiagnostic { get; private set; } = string.Empty;

        public event Action<GameplayState> StateChanged;
        public event Action<GameFlowInitializationStage> InitializationStageChanged;
        public event Action HudPreparationRequested;
        public event Action RestartRequested;
        public event Action ExitRequested;
        public event Action<string> DiagnosticReported;

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

            if (applicationExitAdapter == null)
            {
                applicationExitAdapter = Application.isEditor
                    ? (IApplicationExit)editorApplicationExitAdapter
                    : runtimeApplicationExitAdapter;
            }
        }

        private void Start()
        {
            InitializeNow();
        }

        private void Update()
        {
            if (!initialized || inputReader == null)
            {
                return;
            }

            try
            {
                inputHandler.ProcessFrameInput(inputReader, IsTerminal, () => RequestRestart(), () => RequestExit());
            }
            catch (Exception exception)
            {
                ReportGameplayException("入力取得", exception);
            }
        }

        /// <summary>入力スナップショットをGameFlowの再開・終了入口へ渡します。</summary>
        public void ProcessInput(GameplayInputSnapshot snapshot)
        {
            try
            {
                inputHandler.ProcessInput(snapshot, IsTerminal, () => RequestRestart(), () => RequestExit());
            }
            catch (Exception exception)
            {
                ReportGameplayException("入力処理", exception);
            }
        }

        private void OnDestroy()
        {
            UnsubscribeFromHealthComponents();
        }

        /// <summary>
        /// Bootから検証、登録、スナップショット、体力初期化、HUD準備、Running/終局へ進みます。
        /// </summary>
        public bool InitializeNow()
        {
            try
            {
                return InitializeNowCore();
            }
            catch (Exception exception)
            {
                FailInitialization(BuildGameplayExceptionDiagnostic("初期化", exception));
                return false;
            }
        }

        private bool InitializeNowCore()
        {
            if (initialized)
            {
                return !initializationFailed;
            }

            initialized = true;
            CurrentState = GameplayState.Boot;
            initializationFailed = false;
            playerStartedWithoutHealth = false;
            initializationTrace.Clear();
            SetInitializationStage(GameFlowInitializationStage.Boot);

            SetInitializationStage(GameFlowInitializationStage.Validation);
            if (!ValidateRequiredReferences(out string validationDiagnostic))
            {
                FailInitialization(validationDiagnostic);
                return false;
            }

            SetInitializationStage(GameFlowInitializationStage.Registration);
            sceneReferenceRegistry.ClearRuntimeRegistrations();
            damageService.ConfigureForRuntime(this, gameplayClock, sceneReferenceRegistry);
            gameplayClock?.ConfigureStateProvider(this);
            if (!RegisterCombatants(out string registrationDiagnostic))
            {
                FailInitialization(registrationDiagnostic);
                return false;
            }

            SetInitializationStage(GameFlowInitializationStage.SpawnSnapshot);
            if (!sceneReferenceRegistry.CaptureSpawnSnapshot(out string snapshotDiagnostic))
            {
                FailInitialization(snapshotDiagnostic);
                return false;
            }

            SetInitializationStage(GameFlowInitializationStage.HealthAndEnemyInitialization);
            if (!InitializeCombatants(out string healthDiagnostic))
            {
                FailInitialization(healthDiagnostic);
                return false;
            }

            SubscribeToHealthComponents();
            SetInitializationStage(GameFlowInitializationStage.HudPreparation);
            try
            {
                HudPreparationRequested?.Invoke();
            }
            catch (Exception exception)
            {
                ReportGameplayException("HUD準備通知", exception);
            }

            IsHudReady = sceneReferenceRegistry.PrepareHud(this, out string hudDiagnostic);
            if (!IsHudReady)
            {
                FailInitialization(hudDiagnostic);
                return false;
            }

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

            return !initializationFailed;
        }


        /// <summary>
        /// 公開された状態書き換え入口です。終局状態から別の終局状態へは遷移できません。
        /// </summary>
        public bool TrySetState(GameplayState nextState)
        {
            if (nextState == CurrentState)
            {
                return false;
            }

            if (CurrentState == GameplayState.Victory || CurrentState == GameplayState.Defeat)
            {
                if (nextState != GameplayState.Restarting)
                {
                    ReportDiagnostic("終局状態は後続の状態書き換えで変更できません。", false);
                    return false;
                }
            }

            if (CurrentState == GameplayState.Restarting)
            {
                ReportDiagnostic("再開処理中は状態を変更できません。", false);
                return false;
            }

            if (nextState == GameplayState.Boot && CurrentState != GameplayState.Boot)
            {
                ReportDiagnostic("Boot状態へ実行中に戻ることはできません。", false);
                return false;
            }

            TransitionTo(nextState);
            return true;
        }

        /// <summary>敵集合が空になったときにVictoryを要求します。</summary>
        public bool RequestVictory()
        {
            if (CurrentState != GameplayState.Running)
            {
                return false;
            }

            TransitionTo(GameplayState.Victory);
            return true;
        }

        /// <summary>Player死亡時にDefeatを要求します。</summary>
        public bool RequestDefeat()
        {
            if (CurrentState != GameplayState.Running)
            {
                return false;
            }

            TransitionTo(GameplayState.Defeat);
            return true;
        }

        /// <summary>終局中だけRestartingへ遷移し、後続のシーン再読み込みを通知します。</summary>
        public bool RequestRestart()
        {
            if (!IsTerminal)
            {
                ReportDiagnostic("終局状態以外では再開を要求できません。", false);
                return false;
            }

            GameplayState previousState = CurrentState;
            if (!TrySetState(GameplayState.Restarting))
            {
                return false;
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

                return true;
            }
            catch (Exception exception)
            {
                ReportGameplayException("再開", exception);
                if (CurrentState == GameplayState.Restarting)
                {
                    TransitionTo(previousState);
                }

                return false;
            }
        }

        /// <summary>プラットフォーム終了処理を適切なアダプターへ委譲します。</summary>
public void RequestExit()
        {
            try
            {
                ExitRequested?.Invoke();
                if (applicationExitAdapter == null)
                {
                    applicationExitAdapter = Application.isEditor
                        ? (IApplicationExit)editorApplicationExitAdapter
                        : runtimeApplicationExitAdapter;
                }
                if (applicationExitAdapter == null)
                {
                    ReportDiagnostic("終了アダプターが見つからないため、終了要求を処理できません。", true);
                    return;
                }

                applicationExitAdapter.RequestExit();
            }
            catch (Exception exception)
            {
                ReportGameplayException("終了要求", exception);
            }
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

            try
            {
                StateChanged?.Invoke(nextState);
            }
            catch (Exception exception)
            {
                ReportGameplayException("状態通知", exception);
            }
        }

        [Inject]
        public void Construct(
            SceneReferenceRegistry sceneRegistry = null,
            GameplayClock clock = null,
            DamageService damage = null,
            InputReader input = null,
            IApplicationExit exitAdapter = null)
        {
            if (sceneRegistry != null) sceneReferenceRegistry = sceneRegistry;
            if (clock != null) gameplayClock = clock;
            if (damage != null) damageService = damage;
            if (input != null) inputReader = input;
            if (exitAdapter != null) applicationExitAdapter = exitAdapter;
        }

        private bool ValidateRequiredReferences(out string diagnostic)
        {
            if (sceneReferenceRegistry == null)
            {
                diagnostic = "GameFlowControllerにSceneReferenceRegistry参照がありません。";
                return ReportFailure(diagnostic);
            }

            if (!sceneReferenceRegistry.ResolveSceneReferences())
            {
                diagnostic = sceneReferenceRegistry.LastDiagnostic;
                if (string.IsNullOrEmpty(diagnostic))
                {
                    diagnostic = "シーン参照の検証に失敗しました。";
                }

                return ReportFailure(diagnostic);
            }

            if (damageService == null)
            {
                diagnostic = "GameFlowControllerにDamageService参照がありません。";
                return ReportFailure(diagnostic);
            }

            if (sceneReferenceRegistry.Player == null)
            {
                diagnostic = "Player参照がないためGameFlowを開始できません。";
                return ReportFailure(diagnostic);
            }

            HealthComponent playerHealth = sceneReferenceRegistry.Player.GetComponent<HealthComponent>();
            if (playerHealth == null)
            {
                diagnostic = "PlayerにHealthComponentがないためGameFlowを開始できません。";
                return ReportFailure(diagnostic);
            }

            if (!DamageRequest.IsFinitePositiveAmount(playerHealth.MaximumHealth))
            {
                diagnostic = "Playerの最大体力が0以下または不正なためGameFlowを開始できません。";
                return ReportFailure(diagnostic);
            }

            diagnostic = string.Empty;
            return true;
        }

        private bool RegisterCombatants(out string diagnostic)
        {
            if (!sceneReferenceRegistry.Register(sceneReferenceRegistry.Player))
            {
                diagnostic = sceneReferenceRegistry.LastDiagnostic;
                return false;
            }

            IReadOnlyList<CombatantMarker> enemies = sceneReferenceRegistry.ConfiguredEnemies;
            for (int index = 0; index < enemies.Count; index++)
            {
                CombatantMarker enemy = enemies[index];
                if (enemy == null || !enemy.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (!sceneReferenceRegistry.Register(enemy))
                {
                    diagnostic = sceneReferenceRegistry.LastDiagnostic;
                    return false;
                }
            }

            diagnostic = string.Empty;
            return true;
        }

        private bool InitializeCombatants(out string diagnostic)
        {
            CombatantMarker player = sceneReferenceRegistry.Player;
            HealthComponent playerHealth = player.GetComponent<HealthComponent>();
            playerStartedWithoutHealth = playerHealth.CurrentHealth <= 0f || !playerHealth.IsAlive;
            if (!playerStartedWithoutHealth && !playerHealth.EnterDemo(out diagnostic))
            {
                return false;
            }

            IReadOnlyList<CombatantMarker> enemies = sceneReferenceRegistry.ConfiguredEnemies;
            for (int index = 0; index < enemies.Count; index++)
            {
                CombatantMarker enemy = enemies[index];
                if (enemy == null || !enemy.gameObject.activeInHierarchy)
                {
                    continue;
                }

                HealthComponent enemyHealth = enemy.GetComponent<HealthComponent>();
                if (enemyHealth == null)
                {
                    diagnostic = $"敵「{enemy.gameObject.name}」にHealthComponentがありません。";
                    return false;
                }

                if (!enemyHealth.EnterDemo(out diagnostic))
                {
                    return false;
                }
            }

            diagnostic = string.Empty;
            return true;
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
            try
            {
                InitializationStageChanged?.Invoke(stage);
            }
            catch (Exception exception)
            {
                ReportGameplayException("初期化段階通知", exception);
            }
        }

        private bool ReportFailure(string diagnostic)
        {
            LastDiagnostic = diagnostic;
            DiagnosticReported?.Invoke(diagnostic);
            return false;
        }

        private void ReportDiagnostic(string diagnostic, bool asError)
        {
            if (string.IsNullOrEmpty(diagnostic))
            {
                return;
            }

            LastDiagnostic = diagnostic;
            if (asError)
            {
                Debug.LogError($"[GameFlow診断] {diagnostic}", this);
            }
            else
            {
                Debug.Log($"[GameFlow診断] {diagnostic}", this);
            }

            try
            {
                DiagnosticReported?.Invoke(diagnostic);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[GameFlow診断] 診断通知中の例外を捕捉しました。{exception.GetType().Name}。", this);
            }
        }

        private void FailInitialization(string diagnostic)
        {
            initializationFailed = true;
            LastDiagnostic = string.IsNullOrEmpty(diagnostic) ? "GameFlow初期化に失敗しました。" : diagnostic;
            SetInitializationStage(GameFlowInitializationStage.Failed);
            Debug.LogError($"[GameFlow診断] {LastDiagnostic}", this);
            try
            {
                DiagnosticReported?.Invoke(LastDiagnostic);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[GameFlow診断] 初期化失敗通知中の例外を捕捉しました。{exception.GetType().Name}。", this);
            }
        }


        private static string BuildGameplayExceptionDiagnostic(string operation, Exception exception)
        {
            string exceptionType = exception == null ? "不明な例外" : exception.GetType().Name;
            return $"ゲームフロー{operation}中に予期しない例外を捕捉しました。例外種別: {exceptionType}。RestartとExitは継続可能です。";
        }


        private void ReportGameplayException(string operation, Exception exception)
        {
            string diagnostic = BuildGameplayExceptionDiagnostic(operation, exception);
            LastDiagnostic = diagnostic;
            Debug.LogError($"[GameFlow診断] {diagnostic}", this);
            try
            {
                DiagnosticReported?.Invoke(diagnostic);
            }
            catch (Exception notificationException)
            {
                Debug.LogError($"[GameFlow診断] 例外診断通知中の例外を捕捉しました。{notificationException.GetType().Name}。", this);
            }
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
