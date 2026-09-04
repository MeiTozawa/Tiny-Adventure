using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using TinyAdventure;

namespace TinyAdventure.Tests
{
    /// <summary>
    /// 攻撃系列と攻撃有効ウィンドウのライフサイクルを編集モードで検証します。
    /// </summary>
    public sealed class AttackWindowContractTests
    {
        private GameObject attackerObject;
        private CombatantMarker attacker;
        private GameObject targetObject;
        private CombatantMarker target;

        [SetUp]
        public void SetUp()
        {
            attackerObject = new GameObject("Knight攻撃者");
            attacker = attackerObject.AddComponent<CombatantMarker>();
            targetObject = new GameObject("敵対象");
            targetObject.transform.position = Vector3.forward;
            target = targetObject.AddComponent<CombatantMarker>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(attackerObject);
            Object.DestroyImmediate(targetObject);
        }

        [Test]
        public void AttackWindowOpensAndClosesOnceBeforeCompletionUnderDuplicateEventsCancellationAndTerminalState()
        {
            // **Validates: Requirements 4.5, 4.6**
            const int iterationCount = 100;

            for (int iteration = 1; iteration <= iterationCount; iteration++)
            {
                var tracker = new AttackWindowTracker(attacker, 2f);
                var sequence = new AttackSequence(tracker, AttackSequence.DefaultFallbackCloseNormalizedTime);
                int openedCount = 0;
                int closedCount = 0;
                int completedCount = 0;
                int cancelledCount = 0;

                sequence.WindowOpened += _ => openedCount++;
                sequence.WindowClosed += _ => closedCount++;
                sequence.SequenceCompleted += _ => completedCount++;
                sequence.SequenceCancelled += _ => cancelledCount++;

                Assert.That(
                    sequence.StartSequence(iteration, out string startDiagnostic),
                    Is.True,
                    $"系列{iteration}: {startDiagnostic}");
                Assert.That(sequence.OnAttackWindowOpenEvent(out string openDiagnostic), Is.True, openDiagnostic);

                // 同じAnimator eventの再送は二つ目のウィンドウを開けません。
                Assert.That(sequence.OnAttackWindowOpenEvent(out _), Is.False, $"系列{iteration}: 重複開放が受理されました。");
                Assert.That(openedCount, Is.EqualTo(1), $"系列{iteration}: 開放イベントは一度だけです。");
                Assert.That(tracker.IsWindowOpen, Is.True, $"系列{iteration}: ウィンドウが開いています。");

                // 剣のCollider callbackが同じ対象へ重複して届いても、系列内の命中候補は一つだけです。
                Assert.That(tracker.RegisterTarget(target, out string targetDiagnostic), Is.True, $"系列{iteration}: {targetDiagnostic}");
                Assert.That(tracker.RegisterTarget(target, out _), Is.False, $"系列{iteration}: 重複Collider callbackが受理されました。");

                bool cancelled = iteration % 4 == 0;
                bool terminalForcedClose = iteration % 5 == 0;
                bool normalizedFallback = iteration % 3 == 0 && !cancelled && !terminalForcedClose;
                float closeNormalizedTime;

                if (cancelled)
                {
                    closeNormalizedTime = 0.35f;
                    sequence.Cancel();
                    Assert.That(sequence.Phase, Is.EqualTo(AttackSequencePhase.Cancelled), $"系列{iteration}: 取消後の状態が不正です。");
                }
                else if (terminalForcedClose)
                {
                    closeNormalizedTime = 0.4f;
                    // 終局またはオブジェクト無効化と同じ強制終了経路を検証します。
                    sequence.ForceClose();
                    Assert.That(sequence.Phase, Is.EqualTo(AttackSequencePhase.Cancelled), $"系列{iteration}: 強制終了後の状態が不正です。");
                }
                else if (normalizedFallback)
                {
                    closeNormalizedTime = AttackSequence.DefaultFallbackCloseNormalizedTime;
                    LogAssert.Expect(LogType.Warning, new Regex("\\[攻撃診断\\].*"));
                    sequence.Tick(closeNormalizedTime);
                    Assert.That(sequence.FallbackWindowCloseUsed, Is.True, $"系列{iteration}: normalized timeの保険閉鎖が使われていません。");
                }
                else
                {
                    closeNormalizedTime = 0.5f;
                    Assert.That(sequence.OnAttackWindowCloseEvent(), Is.True, $"系列{iteration}: Animator eventで閉鎖できません。");
                }

                Assert.That(closeNormalizedTime, Is.LessThan(1f), $"系列{iteration}: ウィンドウはclip完了前に閉じます。");
                Assert.That(sequence.IsWindowOpen, Is.False, $"系列{iteration}: ウィンドウが閉じていません。");
                Assert.That(tracker.IsWindowOpen, Is.False, $"系列{iteration}: trackerのウィンドウが閉じていません。");
                Assert.That(closedCount, Is.EqualTo(1), $"系列{iteration}: 閉鎖イベントは一度だけです。");

                // Animator event、Collider callback、終局処理の重複呼び出しを冪等に扱います。
                Assert.That(sequence.OnAttackWindowCloseEvent(), Is.False, $"系列{iteration}: 重複閉鎖が受理されました。");
                if (cancelled || terminalForcedClose)
                {
                    sequence.ForceClose();
                }
                Assert.That(closedCount, Is.EqualTo(1), $"系列{iteration}: 強制終了で閉鎖イベントが重複しました。");

                if (cancelled || terminalForcedClose)
                {
                    Assert.That(completedCount, Is.EqualTo(0), $"系列{iteration}: 取消済み攻撃が完了扱いになりました。");
                    Assert.That(cancelledCount, Is.EqualTo(1), $"系列{iteration}: 取消イベントは一度だけです。");
                }
                else
                {
                    Assert.That(sequence.Complete(out string completeDiagnostic), Is.True, $"系列{iteration}: {completeDiagnostic}");
                    Assert.That(sequence.Phase, Is.EqualTo(AttackSequencePhase.Completed), $"系列{iteration}: 完了後はCompletedです。");
                    Assert.That(completedCount, Is.EqualTo(1), $"系列{iteration}: 完了イベントは一度だけです。");
                    Assert.That(sequence.Complete(out _), Is.False, $"系列{iteration}: 完了後の再完了が受理されました。");

                    // 攻撃完了後は、上位のアニメーション制御がIdleまたはLocomotionへ戻せる終端です。
                    Assert.That(sequence.IsActive, Is.False, $"系列{iteration}: 完了後も攻撃が継続しています。");
                }
            }
        }
    }
}
