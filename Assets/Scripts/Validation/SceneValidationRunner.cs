using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TinyAdventure
{
    /// <summary>
    /// 検証を一度だけ実行し、安定ID付きの日本語結果をConsoleとテスト入口へ公開します。
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-900)]
    public sealed class SceneValidationRunner : MonoBehaviour
    {
        [Header("検証設定")]
        [SerializeField]
        private bool validateOnStart = true;

        [SerializeField]
        private bool logSuccessfulReport;

        // 旧バージョンRunnerのシリアライズ参照を保持し、既存SampleSceneのInspector設定との互換性を維持します。
        [SerializeField]
        private SceneReferenceRegistry sceneReferenceRegistry;

        [SerializeField]
        private GameFlowController gameFlowController;

        [SerializeField]
        private GameplayClock gameplayClock;

        [SerializeField]
        private Camera mainCamera;

        [SerializeField]
        private GameObject hudRoot;

        [SerializeField]
        private CombatantMarker player;

        [SerializeField]
        private List<CombatantMarker> configuredEnemies = new List<CombatantMarker>();

        private DemoSceneValidator validator;

        /// <summary>最後に実行した検証結果です。</summary>
        public ValidationReport LastReport { get; private set; }

        /// <summary>最後の検証が成功したかを返します。</summary>
        public bool IsValid => LastReport != null && LastReport.IsValid;

        /// <summary>既存のテストとInspector検証入口向けの成功フラグです。</summary>
        public bool LastValidationPassed { get; private set; }

        /// <summary>既存の利用者向け診断一覧です。</summary>
        public IReadOnlyList<string> Diagnostics { get; private set; } = new List<string>();

        /// <summary>既存の利用者向け最後の日本語診断です。</summary>
        public string LastDiagnostic { get; private set; } = string.Empty;

        /// <summary>検証結果通知です。</summary>
        public event Action<ValidationReport> ValidationCompleted;

        private void Start()
        {
            if (validateOnStart)
            {
                RunValidation();
            }
        }

        /// <summary>現在のシーンを検証し、結果をConsoleへ記録します。</summary>
        public ValidationReport RunValidation()
        {
            if (validator == null)
            {
                validator = new DemoSceneValidator();
            }

            LastReport = validator.Validate(SceneManager.GetActiveScene());
            LastValidationPassed = LastReport != null && LastReport.IsValid;
            List<string> reportDiagnostics = new List<string>();
            if (LastReport != null)
            {
                for (int index = 0; index < LastReport.Issues.Count; index++)
                {
                    reportDiagnostics.Add(LastReport.Issues[index].Diagnostic);
                }
            }

            Diagnostics = reportDiagnostics;
            LastDiagnostic = reportDiagnostics.Count > 0 ? reportDiagnostics[0] : string.Empty;
            LogReport(LastReport);
            ValidationCompleted?.Invoke(LastReport);
            return LastReport;
        }

        /// <summary>既存のbool検証入口です。</summary>
        public bool ValidateNow()
        {
            return RunValidation() != null && LastValidationPassed;
        }

        /// <summary>失敗結果を日本語診断文字列として取得します。</summary>
        public bool TryValidate(out string diagnostic)
        {
            ValidationReport report = RunValidation();
            diagnostic = report == null ? "検証結果を取得できませんでした。" : report.ToJapaneseSummary();
            return report != null && report.IsValid;
        }

        /// <summary>テストやEditorメニューから指定シーンを検証します。</summary>
        public static ValidationReport ValidateScene(Scene scene)
        {
            return new DemoSceneValidator().Validate(scene);
        }

#if UNITY_EDITOR
        [MenuItem("Tiny Adventure/検証/SampleSceneを検証")]
        private static void ValidateSampleSceneFromMenu()
        {
            ValidationReport report = ValidateScene(SceneManager.GetActiveScene());
            LogReport(report);
            if (!report.IsValid)
            {
                Selection.activeObject = GameObject.Find("GameRoot");
            }
        }
#endif

        private static void LogReport(ValidationReport report)
        {
            if (report == null)
            {
                Debug.LogError("[シーン検証] 検証結果を取得できませんでした。");
                return;
            }

            if (report.IsValid)
            {
                Debug.Log($"[シーン検証] {report.ToJapaneseSummary()}");
            }
            else
            {
                Debug.LogError($"[シーン検証] {report.ToJapaneseSummary()}");
            }

            for (int index = 0; index < report.Issues.Count; index++)
            {
                ValidationIssue issue = report.Issues[index];
                string message = $"[シーン検証][{issue.StableId}] 対象「{issue.TargetName}」: {issue.Diagnostic} 修正案: {issue.RepairSuggestion}";
                if (issue.IsError)
                {
                    Debug.LogError(message);
                }
                else
                {
                    Debug.LogWarning(message);
                }
            }
        }
    }
}
