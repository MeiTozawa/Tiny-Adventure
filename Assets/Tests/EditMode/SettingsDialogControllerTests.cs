using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using TinyAdventure;

namespace TinyAdventure.Tests
{
    /// <summary>
    /// 设置弹窗 SettingsDialogController 的打开/关闭、滑动条同步、重置与光标管理测试。
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
            Assert.That(controller.IsOpen, Is.False, "初始化时设置面板应处于关闭状态。");
            Assert.That(panel.activeSelf, Is.False);
        }

        [Test]
        public void Open_EnablesPanel_AndConfiguresSliderFromSettings()
        {
            testService.SetFov(95f);
            controller.Open();

            Assert.That(controller.IsOpen, Is.True, "调用 Open() 后，面板应被激活。");
            Assert.That(slider.value, Is.EqualTo(95f).Within(0.01f), "Slider 应同步设置服务的 FOV 数值。");
            Assert.That(valueText.text, Is.EqualTo("95°"), "数值文本应格式化显示为带度数符号的字符串。");
        }

        [Test]
        public void SliderChange_UpdatesSettingsServiceAndFovText()
        {
            controller.Open();

            slider.value = 105f;

            Assert.That(testService.CurrentFov, Is.EqualTo(105f).Within(0.01f), "拖动 Slider 应同步更新设置服务的 FOV。");
            Assert.That(valueText.text, Is.EqualTo("105°"), "数值文本应同步更新为新数值。");
        }

        [Test]
        public void ResetButton_RestoresDefaultFovAndSliderPosition()
        {
            testService.SetFov(105f);
            controller.Open();

            resetButton.onClick.Invoke();

            Assert.That(testService.CurrentFov, Is.EqualTo(85f).Within(0.01f), "点击初期化按钮应还原为默认 85°。");
            Assert.That(slider.value, Is.EqualTo(85f).Within(0.01f), "Slider 应恢复为 85°。");
            Assert.That(valueText.text, Is.EqualTo("85°"), "数值文本应恢复为 85°。");
        }

        [Test]
        public void Close_HidesPanel()
        {
            controller.Open();
            Assert.That(controller.IsOpen, Is.True);

            closeButton.onClick.Invoke();

            Assert.That(controller.IsOpen, Is.False, "点击关闭按钮后面板应隐藏。");
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
