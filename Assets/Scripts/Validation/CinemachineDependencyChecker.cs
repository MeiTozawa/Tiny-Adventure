using UnityEngine;
#if UNITY_EDITOR
using System;
using UnityEditor.PackageManager;
#endif

namespace TinyAdventure
{
    /// <summary>
    /// Cinemachine パッケージ解決検査の結果を表します。
    /// </summary>
    public readonly struct CinemachineDependencyValidationResult
    {
        public CinemachineDependencyValidationResult(bool isValid, string diagnostic, string resolvedVersion)
        {
            IsValid = isValid;
            Diagnostic = diagnostic;
            ResolvedVersion = resolvedVersion;
        }

        public bool IsValid { get; }
        public string Diagnostic { get; }
        public string ResolvedVersion { get; }
    }

    /// <summary>
    /// Cinemachine が正式な第三人称カメラとして利用できる解決状態かを検査します。
    /// この検査は通常の Camera を代替として承認しません。
    /// </summary>
    public static class CinemachineDependencyChecker
    {
        public const string PackageName = "com.unity.cinemachine";
        public const string RequiredVersion = "3.1.6";

        /// <summary>
        /// プロジェクト解析後の Cinemachine パッケージ情報を検査します。
        /// </summary>
        public static CinemachineDependencyValidationResult Validate()
        {
#if UNITY_EDITOR
            PackageInfo packageInfo = PackageInfo.FindForPackageName(PackageName);
            if (packageInfo == null)
            {
                return Failure("Cinemachineパッケージが見つかりません。Packages/manifest.json に com.unity.cinemachine 3.1.6 を追加してください。");
            }

            if (!string.Equals(packageInfo.version, RequiredVersion, StringComparison.Ordinal))
            {
                return Failure($"Cinemachineパッケージのバージョンが一致しません。必要: {RequiredVersion}、検出: {packageInfo.version}。");
            }

            if (string.IsNullOrWhiteSpace(packageInfo.resolvedPath))
            {
                return Failure("Cinemachineパッケージの解決情報を取得できません。Package Manager の解決状態を確認してください。");
            }

            return new CinemachineDependencyValidationResult(
                true,
                $"Cinemachineパッケージを確認しました。バージョン: {packageInfo.version}。",
                packageInfo.version);
#else
            return Failure("Cinemachineパッケージの解決状態はUnity Editorで検査してください。");
#endif
        }

        /// <summary>
        /// 検査結果を真偽値と日本語診断として取得します。
        /// </summary>
        public static bool TryValidate(out string diagnostic)
        {
            CinemachineDependencyValidationResult result = Validate();
            diagnostic = result.Diagnostic;
            return result.IsValid;
        }

        private static CinemachineDependencyValidationResult Failure(string diagnostic)
        {
            return new CinemachineDependencyValidationResult(false, diagnostic, string.Empty);
        }
    }
}
