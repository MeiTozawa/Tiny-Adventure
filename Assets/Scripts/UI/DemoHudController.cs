using System;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace TinyAdventure
{
    /// <summary>
    /// SampleSceneのUGUIをGameFlow、Player体力、敵登録簿のイベントだけで更新します。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DemoHudController : MonoBehaviour, IGameplayHudPreparation
    {
        private const string ControlsMessage = "移動: WASD / 左スティック\n攻撃: 左クリック / A\n設定: Tabキー\n再開: Rキー\n終了: Escキー";
        private const string VictoryMessage = "勝利！";
        private const string DefeatMessage = "敗北";
        private const string RestartMessage = "Rキーで再開";

        [Header("UI参照")]
        [SerializeField]
        private Text healthText;

        [SerializeField]
        private Text enemyCountText;

        [SerializeField]
        private Text controlsText;

        [Header("第一人称准星（Reticle）")]
        private GameObject reticle;

        private SettingsDialogController settingsDialog;

        [SerializeField]
        private GameObject victoryPanel;

        [SerializeField]
        private GameObject defeatPanel;

        [SerializeField]
        private Text victoryTitleText;

        [SerializeField]
        private Text victoryRestartText;

        [SerializeField]
        private Text defeatTitleText;

        [SerializeField]
        private Text defeatRestartText;

        private GameFlowController gameFlowController;
        private SceneReferenceRegistry sceneReferenceRegistry;
        private HealthComponent playerHealth;

        private bool subscribed;
        private string currentHealthText = string.Empty;
        private string currentEnemyCountText = string.Empty;
        private string currentControlsText = string.Empty;
        private string currentVictoryTitleText = string.Empty;
        private string currentVictoryRestartText = string.Empty;
        private string currentDefeatTitleText = string.Empty;
        private string currentDefeatRestartText = string.Empty;
        private bool terminalPanelVisible;
        private GameplayState displayedState = GameplayState.Boot;
        private int uiUpdateCount;

        /// <summary>現在表示している体力文言です。テストと診断から参照できます。</summary>
        public string CurrentHealthText => currentHealthText;

        /// <summary>現在表示している敵数文言です。テストと診断から参照できます。</summary>
        public string CurrentEnemyCountText => currentEnemyCountText;

        /// <summary>現在表示している操作説明です。</summary>
        public string CurrentControlsText => currentControlsText;

        /// <summary>勝利パネルのタイトル文言です。</summary>
        public string CurrentVictoryTitleText => currentVictoryTitleText;

        /// <summary>敗北パネルのタイトル文言です。</summary>
        public string CurrentDefeatTitleText => currentDefeatTitleText;

        /// <summary>第一人称用の准星（Reticle）オブジェクトです。</summary>
        public GameObject Reticle => reticle;

        /// <summary>設定ダイアログコントローラーです。</summary>
        public SettingsDialogController SettingsDialog => settingsDialog;

        /// <summary>終局パネルが現在表示されているかを返します。</summary>
        public bool IsTerminalPanelVisible => terminalPanelVisible;

        /// <summary>最後に反映したGameFlow状態です。</summary>
        public GameplayState DisplayedState => displayedState;

        /// <summary>UIを変更した回数です。重複イベントの診断に使用します。</summary>
        public int UiUpdateCount => uiUpdateCount;

        [Inject]
        public void Construct(
            GameFlowController flow = null,
            SceneReferenceRegistry registry = null,
            SettingsDialogController dialog = null)
        {
            if (flow != null) gameFlowController = flow;
            if (registry != null) sceneReferenceRegistry = registry;
            if (dialog != null) settingsDialog = dialog;
        }

        private void Awake()
        {
            reticle = transform.Find("Reticle").gameObject;
        }

        private void Start()
        {
            if (gameFlowController != null && sceneReferenceRegistry != null)
            {
                Prepare(gameFlowController, sceneReferenceRegistry, out _);
            }
        }

        private void OnEnable()
        {
            if (playerHealth != null)
            {
                SubscribeToEvents();
                RefreshUi();
            }
        }

        private void OnDisable()
        {
            UnsubscribeFromEvents();
        }

        private void OnDestroy()
        {
            UnsubscribeFromEvents();
        }

        /// <summary>
        /// GameFlowのHUD準備段階で参照を確定し、イベントを一度だけ購読します。
        /// </summary>
        public bool Prepare(GameFlowController flow, SceneReferenceRegistry registry, out string diagnostic)
        {
            GameFlowController previousFlow = gameFlowController;
            SceneReferenceRegistry previousRegistry = sceneReferenceRegistry;
            HealthComponent previousHealth = playerHealth;
            SettingsDialogController previousSettings = settingsDialog;
            gameFlowController = flow;
            sceneReferenceRegistry = registry;
            if (playerHealth == null && registry.Player != null)
            {
                playerHealth = registry.Player.Health;
            }

            if (previousSettings != null && previousSettings != settingsDialog)
            {
                previousSettings.DialogStateChanged -= HandleSettingsDialogStateChanged;
            }

            if (subscribed && (previousFlow != gameFlowController || previousRegistry != sceneReferenceRegistry || previousHealth != playerHealth || previousSettings != settingsDialog))
            {
                UnsubscribeFromEvents();
            }

            SubscribeToEvents();
            RefreshUi();
            diagnostic = string.Empty;
            return true;
        }

        /// <summary>現在のイベント入力を明示的に反映します。毎フレーム呼び出す用途ではありません。</summary>
        public void RefreshNow()
        {
            RefreshUi();
        }

        private void SubscribeToEvents()
        {
            if (subscribed)
            {
                return;
            }

            gameFlowController.StateChanged += HandleGameFlowStateChanged;
            playerHealth.HealthChanged += HandlePlayerHealthChanged;
            playerHealth.StateChanged += HandlePlayerHealthStateChanged;
            sceneReferenceRegistry.ActiveEnemyCountChanged += HandleActiveEnemyCountChanged;

            if (settingsDialog != null)
            {
                settingsDialog.DialogStateChanged -= HandleSettingsDialogStateChanged;
                settingsDialog.DialogStateChanged += HandleSettingsDialogStateChanged;
            }

            UpdateReticleVisibility();

            subscribed = true;
        }

        private void UnsubscribeFromEvents()
        {
            if (!subscribed)
            {
                return;
            }

            if (gameFlowController != null)
            {
                gameFlowController.StateChanged -= HandleGameFlowStateChanged;
            }

            if (playerHealth != null)
            {
                playerHealth.HealthChanged -= HandlePlayerHealthChanged;
                playerHealth.StateChanged -= HandlePlayerHealthStateChanged;
            }

            if (sceneReferenceRegistry != null)
            {
                sceneReferenceRegistry.ActiveEnemyCountChanged -= HandleActiveEnemyCountChanged;
            }

            if (settingsDialog != null)
            {
                settingsDialog.DialogStateChanged -= HandleSettingsDialogStateChanged;
            }

            subscribed = false;
        }

        private void HandleSettingsDialogStateChanged(bool isOpen)
        {
            UpdateReticleVisibility();
        }

        private void UpdateReticleVisibility()
        {
            bool isSettingsOpen = settingsDialog != null && settingsDialog.IsOpen;
            bool isTerminal = gameFlowController.IsTerminal;
            reticle.SetActive(!isSettingsOpen && !isTerminal);
        }

        private void HandleGameFlowStateChanged(GameplayState nextState)
        {
            RefreshUi();
        }

        private void HandlePlayerHealthChanged(float current, float maximum)
        {
            RefreshUi();
        }

        private void HandlePlayerHealthStateChanged(HealthState nextState)
        {
            RefreshUi();
        }

        private void HandleActiveEnemyCountChanged(int count)
        {
            RefreshUi();
        }

        private void RefreshUi()
        {

            string nextHealthText = $"体力: {FormatValue(playerHealth.CurrentHealth)}/{FormatValue(playerHealth.MaximumHealth)}";
            string nextEnemyCountText = $"残りの敵: {sceneReferenceRegistry.ActiveEnemyCount}";
            GameplayState nextState = gameFlowController.CurrentState;
            bool nextTerminalPanelVisible = gameFlowController.IsTerminal;
            bool nextVictoryVisible = nextState == GameplayState.Victory;
            bool nextDefeatVisible = nextState == GameplayState.Defeat;

            bool changed = !string.Equals(currentHealthText, nextHealthText, StringComparison.Ordinal) ||
                !string.Equals(currentEnemyCountText, nextEnemyCountText, StringComparison.Ordinal) ||
                !string.Equals(currentControlsText, ControlsMessage, StringComparison.Ordinal) ||
                !string.Equals(currentVictoryTitleText, VictoryMessage, StringComparison.Ordinal) ||
                !string.Equals(currentVictoryRestartText, RestartMessage, StringComparison.Ordinal) ||
                !string.Equals(currentDefeatTitleText, DefeatMessage, StringComparison.Ordinal) ||
                !string.Equals(currentDefeatRestartText, RestartMessage, StringComparison.Ordinal) ||
                terminalPanelVisible != nextTerminalPanelVisible ||
                displayedState != nextState;

            currentHealthText = nextHealthText;
            currentEnemyCountText = nextEnemyCountText;
            currentControlsText = ControlsMessage;
            currentVictoryTitleText = VictoryMessage;
            currentVictoryRestartText = RestartMessage;
            currentDefeatTitleText = DefeatMessage;
            currentDefeatRestartText = RestartMessage;
            terminalPanelVisible = nextTerminalPanelVisible;
            displayedState = nextState;

            if (!changed)
            {
                return;
            }

            healthText.text = currentHealthText;
            enemyCountText.text = currentEnemyCountText;
            controlsText.text = currentControlsText;
            victoryTitleText.text = currentVictoryTitleText;
            victoryRestartText.text = currentVictoryRestartText;
            defeatTitleText.text = currentDefeatTitleText;
            defeatRestartText.text = currentDefeatRestartText;
            victoryPanel.SetActive(nextVictoryVisible);
            defeatPanel.SetActive(nextDefeatVisible);
            uiUpdateCount++;
        }

        private static string FormatValue(float value)
        {
            return Mathf.Approximately(value, Mathf.Round(value))
                ? Mathf.RoundToInt(value).ToString()
                : value.ToString("0.##");
        }
    }
}
