using System;
using UnityEngine;

namespace TinyAdventure
{
    /// <summary>
    /// プレイヤーの戦闘入力（攻撃ボタンの押下・先行入力バッファリング・消費）を管理する独立モジュールです。
    /// 単一責任：InputReaderからの入力スナップショットの取得とInputBufferの制御。
    /// </summary>
    [Serializable]
    public sealed class PlayerCombatInputHandler
    {
        private const float DefaultBufferDuration = 0.25f;

        private readonly InputBuffer inputBuffer;

        public InputBuffer Buffer => inputBuffer;

        public PlayerCombatInputHandler(float bufferDuration = DefaultBufferDuration)
        {
            inputBuffer = new InputBuffer(bufferDuration);
        }

        /// <summary>
        /// フレームごとの入力を読み取り、バッファを更新します。
        /// </summary>
        public void ProcessFrameInput(InputReader inputReader, double nowTime, out bool attackPressedThisFrame)
        {
            attackPressedThisFrame = false;
            if (inputReader == null)
            {
                return;
            }

            GameplayInputSnapshot snapshot = inputReader.ReadSnapshot();
            if (snapshot.AttackPressed)
            {
                inputBuffer.BufferAction(InputBuffer.ActionAttack, nowTime);
                attackPressedThisFrame = true;
            }
        }

        /// <summary>
        /// バッファされた攻撃入力があれば消費し、攻撃開始可否を返します。
        /// </summary>
        public bool ConsumeBufferedAttack(double nowTime)
        {
            return inputBuffer.ConsumeAction(InputBuffer.ActionAttack, nowTime);
        }

        /// <summary>
        /// 入力バッファをクリアします。
        /// </summary>
        public void Clear()
        {
            inputBuffer.Clear();
        }
    }
}
