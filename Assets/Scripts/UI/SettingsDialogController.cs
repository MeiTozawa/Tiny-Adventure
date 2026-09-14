using System;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace TinyAdventure
{
    /// <summary>
    /// 游戏设置模态弹窗控制器。
    /// 负责 FOV 滑块调节、度数显示、初期化重置、以及第一人称下鼠标指针锁定/释放与视角输入暂停。
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

        private GameSettingsService settingsService;
        private FirstPersonCameraController cameraController;
        private bool isSubscribed;

        /// <summary>弹窗当前是否处于打开状态。</summary>
        public bool IsOpen => modalPanel != null && modalPanel.activeSelf;

        /// <summary>弹窗开启/关闭状态变更事件（true 为开启，false 为关闭）。</summary>
        public event Action<bool> DialogStateChanged;

        /// <summary>设置服务访问器。</summary>
        public GameSettingsService SettingsService => settingsService ??= GameSettingsService.Instance;

        private void Awake()
        {
            InitializeTextLabels();
            ResolveReferences();
            BindUiEvents();
        }

        private void InitializeTextLabels()
        {
            if (titleText != null) titleText.text = "設定";
            if (fovLabelText != null) fovLabelText.text = "視野角 (FOV)";
            if (minFovText != null) minFovText.text = $"{GameSettingsService.MinFov}°";
            if (maxFovText != null) maxFovText.text = $"{GameSettingsService.MaxFov}°";
            if (resetButton != null)
            {
                var t = resetButton.GetComponentInChildren<Text>();
                if (t != null) t.text = "初期化";
            }
            if (closeButton != null)
            {
                var t = closeButton.GetComponentInChildren<Text>();
                if (t != null) t.text = "閉じる";
            }
            if (hudSettingsButton != null)
            {
                var t = hudSettingsButton.GetComponentInChildren<Text>();
                if (t != null) t.text = "設定 [Tab]";
            }
            SyncSliderFromSettings();
        }

        private void OnEnable()
        {
            BindUiEvents();
        }

        private void OnDisable()
        {
            UnbindUiEvents();
        }

        private void OnDestroy()
        {
            UnbindUiEvents();
        }

        private void Update()
        {
            CheckToggleInput();
        }

        /// <summary>
        /// 测试或动态装配依赖项。
        /// </summary>
        public void Configure(
            GameObject panel,
            Slider slider,
            Text valueText,
            Button closeBtn,
            Button resetBtn,
            GameSettingsService service = null)
        {
            UnbindUiEvents();
            modalPanel = panel;
            fovSlider = slider;
            fovValueText = valueText;
            closeButton = closeBtn;
            resetButton = resetBtn;
            if (service != null)
            {
                settingsService = service;
            }

            BindUiEvents();
        }

        /// <summary>
        /// 打开设置弹窗，释放光标并暂停相机与战斗输入。
        /// </summary>
        public void Open()
        {
            if (modalPanel != null)
            {
                modalPanel.SetActive(true);
            }

            // 第一人称光标释放
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // 暂停相机视线输入
            ResolveCameraController();
            if (cameraController != null)
            {
                cameraController.IsInputSuspended = true;
            }

            // 同步滑条与文本
            SyncSliderFromSettings();
            DialogStateChanged?.Invoke(true);
        }

        /// <summary>
        /// 关闭设置弹窗，重新锁定光标并恢复视角输入。
        /// </summary>
        public void Close()
        {
            if (modalPanel != null)
            {
                modalPanel.SetActive(false);
            }

            // 恢复第一人称光标锁定
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            // 恢复相机视线输入
            ResolveCameraController();
            if (cameraController != null)
            {
                cameraController.IsInputSuspended = false;
            }

            DialogStateChanged?.Invoke(false);
        }

        /// <summary>
        /// 切换弹窗的开闭状态。
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
            if (Keyboard.current == null) return;

            if (Keyboard.current.tabKey.wasPressedThisFrame || Keyboard.current.oKey.wasPressedThisFrame)
            {
                Toggle();
            }
            else if (IsOpen && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Close();
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

        private void ResolveReferences()
        {
            ResolveCameraController();
        }

        private void ResolveCameraController()
        {
            if (cameraController == null)
            {
                cameraController = FindAnyObjectByType<FirstPersonCameraController>();
            }
        }
    }
}
