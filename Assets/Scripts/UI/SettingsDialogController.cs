using System;
using UnityEngine;
using UnityEngine.UI;
using VContainer;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace TinyAdventure
{
    /// <summary>
    /// 設定モーダルダイアログのコントローラー。
    /// FOVスライダー調整、度数表示、初期化リセット、およびカーソルロック/解除と視点入力停止を管理します。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SettingsDialogController : MonoBehaviour
    {
        [Header("UI 参照")]
        [SerializeField] private GameObject modalPanel;
        [SerializeField] private Slider fovSlider;
        [SerializeField] private Text fovValueText;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button resetButton;
        [SerializeField] private Text titleText;
        [SerializeField] private Text fovLabelText;
        [SerializeField] private Text minFovText;
        [SerializeField] private Text maxFovText;
        [SerializeField] private Button hudSettingsButton;
        [SerializeField] private Text resetButtonText;
        [SerializeField] private Text closeButtonText;
        [SerializeField] private Text hudSettingsButtonText;

        public Text ResetButtonText => resetButtonText;
        public Text CloseButtonText => closeButtonText;
        public Text HudSettingsButtonText => hudSettingsButtonText;

        private GameSettingsService settingsService;
        private FirstPersonCameraController cameraController;
        private bool isSubscribed;

        /// <summary>ダイアログが現在開いているか。</summary>
        public bool IsOpen => modalPanel.activeSelf;

        /// <summary>ダイアログ開閉状態の変更イベント（true: 開く, false: 閉じる）。</summary>
        public event Action<bool> DialogStateChanged;

        /// <summary>設定サービス。</summary>
        public GameSettingsService SettingsService => settingsService ??= GameSettingsService.Instance;

        [Inject]
        public void Construct(GameSettingsService settings = null, FirstPersonCameraController camera = null)
        {
            if (settings != null) settingsService = settings;
            if (camera != null) cameraController = camera;
        }

        private void Awake()
        {
            DisableUiNavigation();
            InitializeTextLabels();
        }

        private void InitializeTextLabels()
        {
            titleText.text = "設定";
            fovLabelText.text = "視野角 (FOV)";
            minFovText.text = $"{GameSettingsService.MinFov}°";
            maxFovText.text = $"{GameSettingsService.MaxFov}°";
            resetButtonText.text = "初期化";
            closeButtonText.text = "閉じる";
            hudSettingsButtonText.text = "設定 [Tab]";
            SyncSliderFromSettings();
        }

#if ENABLE_INPUT_SYSTEM
        private InputAction toggleAction;
        private InputAction closeAction;
#endif

        private void OnEnable()
        {
            SetupInputActions();
            BindUiEvents();
        }

        private void OnDisable()
        {
            TeardownInputActions();
            UnbindUiEvents();
        }

        private void OnDestroy()
        {
            DisposeInputActions();
            UnbindUiEvents();
        }

        private void SetupInputActions()
        {
#if ENABLE_INPUT_SYSTEM
            if (toggleAction == null)
            {
                toggleAction = new InputAction("ToggleSettings", InputActionType.Button);
                toggleAction.AddBinding("<Keyboard>/tab");
                toggleAction.AddBinding("<Keyboard>/o");
                toggleAction.performed += HandleTogglePerformed;
            }
            toggleAction.Enable();

            if (closeAction == null)
            {
                closeAction = new InputAction("CloseSettings", InputActionType.Button);
                closeAction.AddBinding("<Keyboard>/escape");
                closeAction.performed += HandleClosePerformed;
            }
            closeAction.Enable();
#endif
        }

        private void TeardownInputActions()
        {
#if ENABLE_INPUT_SYSTEM
            toggleAction?.Disable();
            closeAction?.Disable();
#endif
        }

        private void DisposeInputActions()
        {
#if ENABLE_INPUT_SYSTEM
            if (toggleAction != null)
            {
                toggleAction.performed -= HandleTogglePerformed;
                toggleAction.Dispose();
                toggleAction = null;
            }

            if (closeAction != null)
            {
                closeAction.performed -= HandleClosePerformed;
                closeAction.Dispose();
                closeAction = null;
            }
#endif
        }

#if ENABLE_INPUT_SYSTEM
        private void HandleTogglePerformed(InputAction.CallbackContext context)
        {
            Toggle();
        }

        private void HandleClosePerformed(InputAction.CallbackContext context)
        {
            if (IsOpen)
            {
                Close();
            }
        }
#endif

        /// <summary>テスト用の依存関係注入。</summary>
        public void Configure(
            GameObject panel,
            Slider slider,
            Text valueText,
            Button closeBtn,
            Button resetBtn,
            GameSettingsService service = null,
            Text resetText = null,
            Text closeText = null,
            Text hudBtnText = null,
            Text title = null,
            Text fovLabel = null,
            Text minFov = null,
            Text maxFov = null,
            Button hudBtn = null)
        {
            UnbindUiEvents();
            modalPanel = panel;
            fovSlider = slider;
            fovValueText = valueText;
            closeButton = closeBtn;
            resetButton = resetBtn;
            if (resetText != null) resetButtonText = resetText;
            if (closeText != null) closeButtonText = closeText;
            if (hudBtnText != null) hudSettingsButtonText = hudBtnText;
            if (title != null) titleText = title;
            if (fovLabel != null) fovLabelText = fovLabel;
            if (minFov != null) minFovText = minFov;
            if (maxFov != null) maxFovText = maxFov;
            if (hudBtn != null) hudSettingsButton = hudBtn;
            if (service != null) settingsService = service;

            DisableUiNavigation();
            InitializeTextLabels();
            BindUiEvents();
        }

        private void DisableUiNavigation()
        {
            Navigation noneNav = new() { mode = Navigation.Mode.None };
            fovSlider.navigation = noneNav;
            closeButton.navigation = noneNav;
            resetButton.navigation = noneNav;
            hudSettingsButton.navigation = noneNav;
        }

        /// <summary>設定ダイアログを開き、カーソルを解放してカメラ入力を一時停止します。</summary>
        public void Open()
        {
            modalPanel.SetActive(true);

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (cameraController != null)
            {
                cameraController.IsInputSuspended = true;
            }

            SyncSliderFromSettings();
            DialogStateChanged?.Invoke(true);
        }

        /// <summary>設定ダイアログを閉じ、カーソルを再ロックしてカメラ入力を再開します。</summary>
        public void Close()
        {
            modalPanel.SetActive(false);

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            if (cameraController != null)
            {
                cameraController.IsInputSuspended = false;
            }

            DialogStateChanged?.Invoke(false);
        }

        /// <summary>
        /// ダイアログの開閉状態を切り替えます。
        /// </summary>
        public void Toggle()
        {
            if (IsOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }



        private void BindUiEvents()
        {
            if (isSubscribed) return;

            fovSlider.minValue = GameSettingsService.MinFov;
            fovSlider.maxValue = GameSettingsService.MaxFov;
            fovSlider.wholeNumbers = true;
            fovSlider.onValueChanged.AddListener(HandleSliderValueChanged);

            closeButton.onClick.AddListener(Close);
            resetButton.onClick.AddListener(HandleResetClicked);
            hudSettingsButton.onClick.AddListener(Open);

            isSubscribed = true;
        }

        private void UnbindUiEvents()
        {
            if (!isSubscribed) return;

            fovSlider.onValueChanged.RemoveListener(HandleSliderValueChanged);
            closeButton.onClick.RemoveListener(Close);
            resetButton.onClick.RemoveListener(HandleResetClicked);
            hudSettingsButton.onClick.RemoveListener(Open);

            isSubscribed = false;
        }

        private void HandleSliderValueChanged(float value)
        {
            SettingsService.SetFov(value);
            UpdateValueText(value);
        }

        private void HandleResetClicked()
        {
            SettingsService.ResetToDefault();
            SyncSliderFromSettings();
        }

        private void SyncSliderFromSettings()
        {
            float fov = SettingsService.CurrentFov;
            fovSlider.value = fov;
            UpdateValueText(fov);
        }

        private void UpdateValueText(float fov)
        {
            fovValueText.text = $"{Mathf.RoundToInt(fov)}°";
        }
    }
}
