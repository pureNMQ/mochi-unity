using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Mochi.Unity.Preview.GUI.Tests
{
    public class UIRootTests
    {
        private static readonly string[] LayerNames =
        {
            "HUDLayer", "PageLayer", "ModalLayer", "TransitionLayer", "ToastLayer"
        };

        private static readonly string[] PanelNames =
        {
            "HudPanel", "PagePanel", "ModalPanel", "TransitionPanel", "ToastPanel"
        };

        private readonly List<GameObject> objectsToDestroy = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject target in objectsToDestroy)
            {
                if (target != null)
                {
                    Object.DestroyImmediate(target);
                }
            }

            objectsToDestroy.Clear();
        }

        // 验收条件 AC15A:当面板分属不同层，则渲染顺序恒为 HUD < Page < Modal < Transition < Toast。
        [UnityTest]
        public IEnumerator RenderOrder_IsHudPageModalTransitionToast()
        {
            return UniTask.ToCoroutine(async () =>
            {
                ClearLeftoverRoots();
                UIRoot root = Track(new GameObject("UIRoot")).AddComponent<UIRoot>();

                RectTransform[] layers = FindFiveLayers(root);
                Assert.That(root.HUD, Is.SameAs(layers[0]));
                Assert.That(root.Page, Is.SameAs(layers[1]));
                Assert.That(root.Modal, Is.SameAs(layers[2]));
                Assert.That(root.Transition, Is.SameAs(layers[3]));

                await AssertLayerRenderOrderAsync(layers);
            });
        }

        // 验收条件 AC22A:当 UI 根被销毁后重建，则五层恢复且顺序不变。
        [UnityTest]
        public IEnumerator DestroyAndRebuild_RestoresFiveLayersAndOrder()
        {
            return UniTask.ToCoroutine(async () =>
            {
                ClearLeftoverRoots();
                UIRoot first = UIRoot.Instance;
                Track(first.gameObject);

                await AssertLayerRenderOrderAsync(FindFiveLayers(first));

                Object.Destroy(first.gameObject);
                await UniTask.Yield();
                Assert.That(first == null, Is.True);

                UIRoot rebuilt = UIRoot.Instance;
                Assert.That(rebuilt, Is.Not.SameAs(first));
                Track(rebuilt.gameObject);

                await AssertLayerRenderOrderAsync(FindFiveLayers(rebuilt));
            });
        }

        //直接检查画布层级而非经层属性访问，避免属性懒重建掩盖装配缺失
        private static RectTransform[] FindFiveLayers(UIRoot root)
        {
            Canvas canvas = root.GetComponentInChildren<Canvas>();
            Assert.That(canvas, Is.Not.Null);
            Assert.That(canvas.transform.parent, Is.SameAs(root.transform));
            Assert.That(canvas.transform.childCount, Is.EqualTo(LayerNames.Length));

            RectTransform[] layers = new RectTransform[LayerNames.Length];
            for (int i = 0; i < LayerNames.Length; i++)
            {
                Transform layer = canvas.transform.Find(LayerNames[i]);
                Assert.That(layer, Is.Not.Null, LayerNames[i] + " 缺失");
                Assert.That(layer.GetSiblingIndex(), Is.EqualTo(i), LayerNames[i] + " 排序位");
                layers[i] = (RectTransform)layer;
            }

            return layers;
        }

        private static async UniTask AssertLayerRenderOrderAsync(RectTransform[] layers)
        {
            Image[] panels = new Image[layers.Length];
            for (int i = 0; i < layers.Length; i++)
            {
                panels[i] = CreateLayerPanel(layers[i], PanelNames[i]);
            }

            Canvas.ForceUpdateCanvases();
            //depth 由画布更新管线计算，ForceUpdateCanvases 后仍需等一帧才可读到有效值
            await UniTask.Yield();

            for (int i = 0; i < panels.Length; i++)
            {
                Assert.That(panels[i].depth, Is.GreaterThanOrEqualTo(0), PanelNames[i] + " depth 未更新");
            }

            for (int i = 1; i < panels.Length; i++)
            {
                Assert.That(panels[i - 1].depth, Is.LessThan(panels[i].depth),
                    PanelNames[i - 1] + " 应先于 " + PanelNames[i] + " 渲染");
            }
        }

        private static Image CreateLayerPanel(RectTransform layer, string panelName)
        {
            GameObject panel = new GameObject(panelName, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(layer, false);

            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image image = panel.GetComponent<Image>();
            image.color = Color.white;
            return image;
        }

        private static void ClearLeftoverRoots()
        {
            foreach (UIRoot leftover in Object.FindObjectsByType<UIRoot>(FindObjectsSortMode.None))
            {
                Object.DestroyImmediate(leftover.gameObject);
            }
        }

        private GameObject Track(GameObject target)
        {
            objectsToDestroy.Add(target);
            return target;
        }
    }
}
