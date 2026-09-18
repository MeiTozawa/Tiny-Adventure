using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using TinyAdventure;

namespace TinyAdventure.Tests
{
    /// <summary>
    /// 設定ダイアログ SettingsDialogController の開閉・スライダー同期・初期化リセット・カーソル管理テスト。
    /// </summary>
    public sealed class SettingsDialogControllerTests
    {
        private GameObject root;
        private GameObject panel;
        private Slider slider;
        private Text valueText;
        private Button closeButton;
        private Button resetButton;
        private SettingsDialogController controller;
        private GameSettingsService testService;

        private sealed class MockStorage : ISettingsStorage
        {
            public float Value = 85f;
            public float GetFloat(string key, float defaultValue) => Value;
            public void SetFloat(string key, float val) => Value = val;
            public void Save() { }
        }

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("SettingsRoot");
            panel = new GameObject("ModalPanel");
            panel.transform.SetParent(root.transform);
            panel.SetActive(false);

            GameObject sliderObj = new GameObject("FovSlider");
            sliderObj.transform.SetParent(panel.transform);
            slider = sliderObj.AddComponent<Slider>();
            slider.minValue = 60f;
            slider.maxValue = 110f;
            slider.value = 85f;

            GameObject textObj = new GameObject("FovValueText");
            textObj.transform.SetParent(panel.transform);
            valueText = textObj.AddComponent<Text>();

            GameObject closeObj = new GameObject("CloseButton");
            closeObj.transform.SetParent(panel.transform);
            closeButton = closeObj.AddComponent<Button>();

            GameObject resetObj = new GameObject("ResetButton");
            resetObj.transform.SetParent(panel.transform);
            resetButton = resetObj.AddComponent<Button>();

            testService = new GameSettingsService(new MockStorage());

            controller = root.AddComponent<SettingsDialogController>();
            controller.Configure(panel, slider, valueText, closeButton, resetButton, testService);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
        }

        [Test]
        public void InitialState_IsClosed()
        {
            Assert.That(controller.IsOpen, Is.False, "初期状態では設定パネルが閉じている必要があります。");
            Assert.That(panel.activeSelf, Is.False);
        }

        [Test]
        public void Open_EnablesPanel_AndConfiguresSliderFromSettings()
        {
            testService.SetFov(95f);
            controller.Open();

            Assert.That(controller.IsOpen, Is.True, "Open() 呼び出し後にパネルがアクティブになる必要があります。");
            Assert.That(slider.value, Is.EqualTo(95f).Within(0.01f), "Slider は設定サービスの FOV 値と同期する必要があります。");
            Assert.That(valueText.text, Is.EqualTo("95°"), "数値テキストは度数記号付きでフォーマット表示される必要があります。");
        }

        [Test]
        public void SliderChange_UpdatesSettingsServiceAndFovText()
        {
            controller.Open();

            slider.value = 105f;

            Assert.That(testService.CurrentFov, Is.EqualTo(105f).Within(0.01f), "Slider 操作により設定サービスの FOV が更新される必要があります。");
            Assert.That(valueText.text, Is.EqualTo("105°"), "数値テキストが新しい値へ同期更新される必要があります。");
        }

        [Test]
        public void ResetButton_RestoresDefaultFovAndSliderPosition()
        {
            testService.SetFov(105f);
            controller.Open();

            resetButton.onClick.Invoke();

            Assert.That(testService.CurrentFov, Is.EqualTo(85f).Within(0.01f), "初期化ボタン押下でデフォルトの 85° にリセットされる必要があります。");
            Assert.That(slider.value, Is.EqualTo(85f).Within(0.01f), "Slider が 85° に復帰する必要があります。");
            Assert.That(valueText.text, Is.EqualTo("85°"), "数値テキストが 85° に復帰する必要があります。");
        }

        [Test]
        public void Close_HidesPanel()
        {
            controller.Open();
            Assert.That(controller.IsOpen, Is.True);

            closeButton.onClick.Invoke();

            Assert.That(controller.IsOpen, Is.False, "閉じるボタン押下でパネルが非表示になる必要があります。");
            Assert.That(panel.activeSelf, Is.False);
        }

        [Test]
        public void DialogStateChanged_EventFiresOnOpenAndClose()
        {
            int stateChangedCount = 0;
            bool lastState = false;
            controller.DialogStateChanged += state =>
            {
                stateChangedCount++;
                lastState = state;
            };

            controller.Open();
            Assert.That(stateChangedCount, Is.EqualTo(1));
            Assert.That(lastState, Is.True);

            controller.Close();
            Assert.That(stateChangedCount, Is.EqualTo(2));
            Assert.That(lastState, Is.False);
        }
    }
}
