using NUnit.Framework;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// Gameplay入力入口のアクション、左クリックバインド、実行時有効化を検証します。
    /// </summary>
    public sealed class InputReaderContractTests
    {
        private GameObject inputObject;

        [SetUp]
        public void SetUp()
        {
            inputObject = new GameObject("入力入口検証");
        }

        [TearDown]
        public void TearDown()
        {
            if (inputObject != null)
            {
                var reader = inputObject.GetComponent<InputReader>();
                reader?.Dispose();
                Object.DestroyImmediate(inputObject);
            }
        }

        [Test]
        public void GameplayAttackHasMouseBindingAndEnabledInputMap()
        {
            InputReader reader = inputObject.AddComponent<InputReader>();

            reader.Initialize();
            Assert.That(reader.IsReady, Is.True, "InputReaderがGameplay入力入口として準備完了になっていません。");

            Assert.That(reader.IsGameplayMapEnabled, Is.True, "Gameplayアクションマップが有効になっていません。");
            Assert.That(reader.IsAttackActionEnabled, Is.True, "Gameplay/Attackアクションが有効になっていません。");
            Assert.That(reader.HasMouseAttackBinding, Is.True, "Gameplay/Attackに<Mouse>/leftButtonバインドがありません。");
        }
    }
}
