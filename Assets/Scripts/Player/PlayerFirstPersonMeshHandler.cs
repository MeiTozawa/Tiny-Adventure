using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace TinyAdventure
{
    /// <summary>
    /// 第一人称視点におけるプレイヤーのメッシュ（頭部・頭盔・胸甲・マント・両腕・両脚）の描画状態を管理します。
    /// 第一人称時はこれらを ShadowsOnly（影のみ描画）に切り替えることで、カメラへの穿孔や近クリップ面での
    /// 激しいポリゴン切断・画面ブレ、低頭時の浮遊足・透明胴体の違和感、および視口武器（Viewmodel）と重複する
    /// 空手腕の露出を防ぎつつ、地面へのキャラクター全身の影投影を維持します。
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class PlayerFirstPersonMeshHandler : MonoBehaviour
    {
        private static readonly string[] DefaultCulledPartNames =
        {
            "Knight_Head",
            "Knight_Helmet",
            "Knight_HelmetVisor",
            "Knight_Body",
            "Knight_Cape",
            "Knight_ArmLeft",
            "Knight_ArmRight",
            "Knight_LegLeft",
            "Knight_LegRight"
        };

        [Header("非表示対象（頭部・頭盔・胸甲・マント・両腕・両脚）")]
        [Tooltip("第一人称時にShadowsOnlyへ切り替える頭部・頭盔・鎧・マント・両腕・両脚のRenderer一覧です。未設定時は自動検出します。")]
        [FormerlySerializedAs("headRenderers")]
        [SerializeField]
        private List<Renderer> culledRenderers = new();

        [Header("初期視点設定")]
        [SerializeField]
        private bool startInFirstPerson = true;

        private bool isFirstPerson;

        /// <summary>現在第一人称のメッシュ遮蔽状態が適用されているかを示します。</summary>
        public bool IsFirstPerson => isFirstPerson;

        /// <summary>管理対象のRenderer一覧です。</summary>
        public IReadOnlyList<Renderer> CulledRenderers => culledRenderers;

        /// <summary>管理対象の頭部Renderer一覧です（後方互換性）。</summary>
        public IReadOnlyList<Renderer> HeadRenderers => culledRenderers;

        private void Awake()
        {
            if (culledRenderers.Count == 0)
            {
                AutoDiscoverCulledRenderers();
            }
            SetFirstPersonMode(startInFirstPerson);
        }

        private void OnEnable()
        {
            SetFirstPersonMode(startInFirstPerson);
        }

        /// <summary>
        /// 第一人称モードの可視性を切り替えます。
        /// </summary>
        public void SetFirstPersonMode(bool firstPerson)
        {
            if (culledRenderers.Count == 0)
            {
                AutoDiscoverCulledRenderers();
            }

            isFirstPerson = firstPerson;
            ShadowCastingMode targetMode = firstPerson ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;

            foreach (var t in culledRenderers)
            {
                t.shadowCastingMode = targetMode;
            }
        }

        /// <summary>
        /// テストや動的初期化向けに対象Rendererを設定します。
        /// </summary>
        public void ConfigureRenderers(IEnumerable<Renderer> renderers)
        {
            culledRenderers.Clear();
            if (renderers != null)
            {
                culledRenderers.AddRange(renderers);
            }
            SetFirstPersonMode(isFirstPerson);
        }

        private void AutoDiscoverCulledRenderers()
        {
            Renderer[] allRenderers = GetComponentsInChildren<Renderer>(true);

            foreach (var r in allRenderers)
            {
                if (r == null) continue;

                var r1 = r;
                if (!DefaultCulledPartNames.Any(t => r1.name.Equals(t, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                if (!culledRenderers.Contains(r))
                {
                    culledRenderers.Add(r);
                }
            }
        }
    }
}
