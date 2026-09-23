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
        public bool IsOpen => modalPanel != null && modalPanel.activeSelf;

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
            if (titleText != null) titleText.text = "設定";
            if (fovLabelText != null) fovLabelText.text = "視野角 (FOV)";
            if (minFovText != null) minFovText.text = $"{GameSettingsService.MinFov}°";
            if (maxFovText != null) maxFovText.text = $"{GameSettingsService.MaxFov}°";
            var rt = ResetButtonText;
            if (rt != null) rt.text = "初期化";
            var ct = CloseButtonText;
            if (ct != null) ct.text = "閉じる";
            var ht = HudSettingsButtonText;
            if (ht != null) ht.text = "設定 [Tab]";
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
            }
            toggleAction.Enable();

            if (closeAction == null)
            {
                closeAction = new InputAction("CloseSettings", InputActionType.Button);
                closeAction.AddBinding("<Keyboard>/escape");
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
            toggleAction?.Dispose();
            toggleAction = null;
            closeAction?.Dispose();
            closeAction = null;
#endif
        }

        private void Update()
        {
            CheckToggleInput();
        }

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
            Text hudBtnText = null)
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
            if (service != null)
            {
                settingsService = service;
            }

            DisableUiNavigation();
            BindUiEvents();
        }

        private void DisableUiNavigation()
        {
            Navigation noneNav = new Navigation { mode = Navigation.Mode.None };
            if (fovSlider != null) fovSlider.navigation = noneNav;
            if (closeButton != null) closeButton.navigation = noneNav;
            if (resetButton != null) resetButton.navigation = noneNav;
            if (hudSettingsButton != null) hudSettingsButton.navigation = noneNav;
        }

        /// <summary>設定ダイアログを開き、カーソルを解放してカメラ入力を一時停止します。</summary>
        public void Open()
        {
            if (modalPanel != null)
            {
                modalPanel.SetActive(true);
            }

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
            if (modalPanel != null)
            {
                modalPanel.SetActive(false);
            }

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

        private void CheckToggleInput()
        {
#if ENABLE_INPUT_SYSTEM
            bool togglePressed = (toggleAction != null && toggleAction.WasPressedThisFrame())
                || (Keyboard.current != null && (Keyboard.current.tabKey.wasPressedThisFrame || Keyboard.current.oKey.wasPressedThisFrame));

            if (togglePressed)
            {
                Toggle();
                return;
            }

            if (IsOpen)
            {
                bool closePressed = (closeAction != null && closeAction.WasPressedThisFrame())
                    || (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame);

                if (closePressed)
                {
                    Close();
                    return;
                }
            }
#endif
        }

        private void BindUiEvents()
        {
            if (isSubscribed) return;

            if (fovSlider != null)
            {
                fovSlider.minValue = GameSettingsService.MinFov;
                fovSlider.maxValue = GameSettingsService.MaxFov;
                fovSlider.wholeNumbers = true;
                fovSlider.onValueChanged.AddListener(HandleSliderValueChanged);
            }

            if (closeButton != null)
            {
                closeButton.onClick.AddListener(Close);
            }

            if (resetButton != null)
            {
                resetButton.onClick.AddListener(HandleResetClicked);
            }

            if (hudSettingsButton != null)
            {
                hudSettingsButton.onClick.AddListener(Open);
            }

            isSubscribed = true;
        }

        private void UnbindUiEvents()
        {
            if (!isSubscribed) return;

            if (fovSlider != null)
            {
                fovSlider.onValueChanged.RemoveListener(HandleSliderValueChanged);
            }

            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(Close);
            }

            if (resetButton != null)
            {
                resetButton.onClick.RemoveListener(HandleResetClicked);
            }

            if (hudSettingsButton != null)
            {
                hudSettingsButton.onClick.RemoveListener(Open);
            }

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
            if (fovSlider != null)
            {
                fovSlider.value = fov;
            }
            UpdateValueText(fov);
        }

        private void UpdateValueText(float fov)
        {
            if (fovValueText != null)
            {
                fovValueText.text = $"{Mathf.RoundToInt(fov)}°";
            }
        }
    }
}
