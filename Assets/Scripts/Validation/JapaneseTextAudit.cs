using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TinyAdventure
{
    /// <summary>
    /// 利用者向け文字列に日本語が含まれ、機能用語が一貫していることを検査します。
    /// </summary>
    public static class JapaneseTextAudit
    {
        private static readonly string[] RequiredTerms =
        {
            "Knight",
            "敵",
            "体力",
            "攻撃",
            "勝利",
            "敗北",
            "再開",
            "NavMesh"
        };

        private static readonly string[] RequiredHudTerms =
        {
            "敵",
            "体力",
            "攻撃",
            "勝利",
            "敗北",
            "再開"
        };

        /// <summary>文字列に日本語の文字が一つ以上含まれるかを返します。</summary>
        public static bool ContainsJapanese(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if ((character >= '\u3040' && character <= '\u30ff') ||
                    (character >= '\u3400' && character <= '\u9fff'))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>空でなく、日本語を含む利用者向け文字列かを検査します。</summary>
        public static bool ValidateText(string value, string targetName, out string diagnostic)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                diagnostic = $"対象「{GetTargetName(targetName)}」の利用者向け文字列が空です。日本語の文言を設定してください。";
                return false;
            }

            if (!ContainsJapanese(value))
            {
                diagnostic = $"対象「{GetTargetName(targetName)}」の利用者向け文字列に日本語がありません。日本語の文言へ修正してください。";
                return false;
            }

            diagnostic = string.Empty;
            return true;
        }

        /// <summary>
        /// 指定シーンのHUD文字列を検査します。未初期化の空文字列はGameFlow準備前として保留します。
        /// </summary>
        public static ValidationReport AuditScene(Scene scene)
        {
            ValidationReport report = new ValidationReport();
            AuditScene(scene, report, FindHudRoot(scene));
            return report;
        }

        /// <summary>既存の検証結果へHUDと診断文字列の監査結果を追加します。</summary>
        public static void AuditScene(Scene scene, ValidationReport report, GameObject hudRoot)
        {
            if (report == null)
            {
                return;
            }

            if (!scene.IsValid())
            {
                report.AddError(
                    "SCN-TEXT-SCENE",
                    "SampleScene",
                    "有効なSampleSceneを開いてから文字列監査を実行してください。",
                    "文字列監査対象のシーンが無効です。");
                return;
            }

            if (hudRoot == null)
            {
                report.AddError(
                    "SCN-TEXT-HUD",
                    "HUDRoot",
                    "UI/HUDRootを作成し、DemoHudControllerを接続してください。",
                    "HUDRootが見つからないため日本語文字列を監査できません。");
                return;
            }

            Text[] texts = hudRoot.GetComponentsInChildren<Text>(true);
            if (texts.Length == 0)
            {
                report.AddError(
                    "SCN-TEXT-COMPONENT",
                    hudRoot.name,
                    "HUDRoot配下へUGUI Textを配置し、各表示参照を接続してください。",
                    "HUDRoot配下に監査対象のTextがありません。");
                return;
            }

            for (int index = 0; index < texts.Length; index++)
            {
                Text text = texts[index];
                if (text == null || string.IsNullOrWhiteSpace(text.text))
                {
                    // GameFlowControllerがHUDを準備する前はTextが空のため、参照検査に任せます。
                    continue;
                }

                if (!ValidateText(text.text, text.gameObject.name, out string diagnostic))
                {
                    report.AddError(
                        "SCN-TEXT-JAPANESE-" + text.gameObject.name,
                        text.gameObject.name,
                        "HUD文言を日本語へ修正し、Knight、敵、体力、攻撃、勝利、敗北、再開の用語を統一してください。",
                        diagnostic);
                }
                else
                {
                    report.AddCheck();
                }
            }

            ValidateTermConsistency(texts, report);
        }

        /// <summary>任意の利用者向け文言集合に必須用語が含まれるかを検査します。</summary>
        public static bool ValidateRequiredTerms(IEnumerable<string> values, out IReadOnlyList<string> diagnostics)
        {
            return ValidateTerms(values, RequiredTerms, out diagnostics);
        }

        private static bool ValidateTerms(IEnumerable<string> values, string[] requiredTerms, out IReadOnlyList<string> diagnostics)
        {
            List<string> results = new List<string>();
            List<string> collected = new List<string>();
            if (values != null)
            {
                foreach (string value in values)
                {
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        collected.Add(value);
                    }
                }
            }

            for (int index = 0; index < requiredTerms.Length; index++)
            {
                string term = requiredTerms[index];
                bool found = false;
                for (int valueIndex = 0; valueIndex < collected.Count; valueIndex++)
                {
                    if (collected[valueIndex].IndexOf(term, StringComparison.Ordinal) >= 0)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    results.Add($"必須用語「{term}」が利用者向け文言にありません。用語を統一してください。");
                }
            }

            diagnostics = results;
            return results.Count == 0;
        }

        private static void ValidateTermConsistency(Text[] texts, ValidationReport report)
        {
            List<string> values = new List<string>();
            for (int index = 0; index < texts.Length; index++)
            {
                if (texts[index] != null && !string.IsNullOrWhiteSpace(texts[index].text))
                {
                    values.Add(texts[index].text);
                }
            }

            // 文字列がまだGameFlow準備前なら、実行時のHUD準備完了後に再監査できます。
            if (values.Count == 0)
            {
                return;
            }

            if (!ValidateTerms(values, RequiredHudTerms, out IReadOnlyList<string> diagnostics))
            {
                for (int index = 0; index < diagnostics.Count; index++)
                {
                    report.AddWarning(
                        "SCN-TEXT-TERM-" + index,
                        "HUDRoot",
                        "HUD文言へ必須用語を追加し、検証結果と同じ日本語用語を使用してください。",
                        diagnostics[index]);
                }
            }
        }

        private static GameObject FindHudRoot(Scene scene)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int index = 0; index < roots.Length; index++)
            {
                Transform child = FindChildByPath(roots[index].transform, "UI/HUDRoot");
                if (child != null)
                {
                    return child.gameObject;
                }
            }

            return null;
        }

        private static Transform FindChildByPath(Transform root, string path)
        {
            if (root == null || string.IsNullOrEmpty(path))
            {
                return null;
            }

            string[] parts = path.Split('/');
            Transform current = root;
            int startIndex = string.Equals(current.name, parts[0], StringComparison.Ordinal) ? 1 : 0;
            for (int index = startIndex; index < parts.Length; index++)
            {
                current = current.Find(parts[index]);
                if (current == null)
                {
                    return null;
                }
            }

            return current;
        }

        private static string GetTargetName(string targetName)
        {
            return string.IsNullOrWhiteSpace(targetName) ? "不明" : targetName;
        }
    }
}
