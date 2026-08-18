using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Mochi;

namespace Mochi.Unity.GUI
{
    /// <summary>
    /// UI管理器，用于管理UI
    /// </summary>
    public class UIManager : Singleton<UIManager>
    {
        private FreeStack<Panel> panelStack = new FreeStack<Panel>();
        private Dictionary<Type, Panel> panelDic = new Dictionary<Type, Panel>();
        private Dictionary<UILayer, GameObject> canvasDic = new Dictionary<UILayer, GameObject>();
        
        public bool IsStackEmpty => panelStack.Count == 0;

        public UIManager()
        {
            CreateLayerCanvas();

        }

        public T ShowPanel<T>() where T : Panel
        {
            var panel = CreateOrGetPanel<T>();

            if (panel.IsShow)
            {
                if (panel.Layer == UILayer.Default && panel != panelStack.Peek())
                {
                    panelStack.Remove(panel);
                }
                else
                {
                    return panel as T;
                }
            }

            panel.Show();

            if (panel.Layer == UILayer.Default)
            {
                if (panelStack.Count > 0)
                {
                    panelStack.Peek().Hide();
                }
                panelStack.Push(panel);
            }

            return panel as T;
        }

        public void HidePanel<T>() where T : Panel
        {
            if (panelDic.TryGetValue(typeof(T), out var panel))
            {
                if (!panel.IsShow) return;

                if (panel.Layer == UILayer.Default)
                {
                    //位于栈顶时，使用BackPanel方法隐藏
                    if (panelStack.Peek() == panel)
                    {
                        BackPanel();
                        return;
                    }
                    else
                    {
                        panelStack.Remove(panel);
                    }
                }

                panel.Hide();
                if (!panel.IsCache)
                {
                    panelDic.Remove(panel.GetType());
                    panel.Destroy();
                }
            }
        }

        public void BackPanel()
        {
            if (panelStack.Count > 0)
            {
                var panel = panelStack.Pop();

                panel.Hide();
                if (!panel.IsCache)
                {
                    panelDic.Remove(panel.GetType());
                    panel.Destroy();
                }

                if (panelStack.Count > 0)
                {
                    panelStack.Peek().Show();
                }
            }
        }

        public void Clear(UILayer layer = UILayer.All)
        {
            if ((layer & UILayer.Default) != 0)
            {
                panelStack.Clear();

            }

            List<Panel> panels = new List<Panel>(panelDic.Values);
            foreach (var panel in panels)
            {
                if ((layer & panel.Layer) != 0)
                {
                    panel.Destroy();
                }
            }

            foreach (var panel in panels)
            {
                panelDic.Remove(panel.GetType());
            }
        }


        public GameObject GetCanvasWithLayer(UILayer layer)
        {
            return canvasDic[layer];
        }

        public T GetPanel<T>() where T : Panel
        {
            if (panelDic.TryGetValue(typeof(T), out var panel))
            {
                return panel as T;
            }
            else
            {
                return null;
            }
        }

        private void CreateLayerCanvas()
        {
            int layerOrder = 0;
            GameObject uiRoot = new GameObject("UIRoot");
            GameObject.DontDestroyOnLoad(uiRoot);

            foreach (UILayer layer in Enum.GetValues(typeof(UILayer)))
            {
                if (layer == UILayer.None || layer == UILayer.All)
                {
                    continue;
                }

                var canvas = new GameObject($"Canvas_{layer}").AddComponent<Canvas>();
                canvas.transform.SetParent(uiRoot.transform);
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = layerOrder;
                layerOrder += 100;
                var canvasScaler = canvas.gameObject.AddComponent<CanvasScaler>();
                canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                canvasScaler.referenceResolution = new Vector2(1920, 1080);
                canvas.gameObject.AddComponent<GraphicRaycaster>();
                canvasDic.Add(layer, canvas.gameObject);
            }


            var eventSystem = new GameObject("EventSystem").AddComponent<EventSystem>();
            eventSystem.transform.SetParent(uiRoot.transform);
            eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
        }

        private Panel CreateOrGetPanel<T>() where T : Panel
        {
            if (panelDic.TryGetValue(typeof(T), out var panel))
            {
                return panel;
            }
            else
            {
                panel = Activator.CreateInstance(typeof(T), this) as Panel;

                if (!canvasDic.TryGetValue(panel.Layer, out var canvas))
                {
                    Debug.LogError($"Panel {panel.Name} layer {panel.Layer} not found");
                    panel.Destroy();
                    return null;
                }

                panel.Init(canvas).Forget();
                panelDic.Add(typeof(T), panel);
                return panel;
            }
        }

        private void MoveToFront(Panel panel)
        {
            if (panel.Layer != UILayer.Default) return;

        }
    }
}
