using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using System;

namespace Mochi.Unity.GUI
{
    public abstract class Panel
    {
        public UIManager Manager { get; private set; }
        public bool IsInit { get; private set; } = false;
        public bool IsDestroyed { get; private set; } = false;
        public bool IsShow { get; private set; } = false;
        public GameObject Canvas { get; private set; } = null;
        public GameObject Root { get; protected set; } = null;

        public event Action<Panel> OnInitComplete;

        public abstract string Name { get; }
        public virtual UILayer Layer { get; } = UILayer.Default;
        public abstract bool IsCache { get; }

        private bool isBeforeInitShow = false;

        private Dictionary<string, Transform> childrenDict;

        public Panel(UIManager manager)
        {
            Manager = manager;
            childrenDict = new Dictionary<string, Transform>();
        }

        /// <summary>
        /// 初始化面板，在Show之前调用，一个实例只调用一次，重复调用无效
        /// </summary>
        public async UniTaskVoid Init(GameObject canvas)
        {
            if (IsDestroyed)
            {
                Debug.LogError($"Panel {Name} is destroyed");
                return;
            }
            if (IsInit) return;
            Canvas = canvas;
            Root = await LoadView();

            if (Root != null)
            {
                Root.transform.SetParent(Canvas.transform, false);
                QueryChildWithCache(Root.transform);
                BindComponent();
            }

            await OnInit();

            IsInit = true;

            if (isBeforeInitShow)
            {
                Show();
            }
            else
            {
                Hide();
            }

            OnInitComplete?.Invoke(this);
        }
        public void Show()
        {
            if (!IsInit)
            {
                isBeforeInitShow = true;
                return;
            }

            if (IsShow) return;

            if (IsDestroyed)
            {
                Debug.LogError($"Panel {Name} is destroyed");
                return;
            }

            IsShow = true;

            if (Root != null)
            {
                Root.transform.SetAsLastSibling();
                Root.SetActive(true);
            }
            OnShow();

        }
        public void Hide()
        {
            if (!IsInit)
            {
                isBeforeInitShow = false;
                return;
            }

            if (!IsShow) return;

            if (IsDestroyed)
            {
                Debug.LogError($"Panel {Name} is destroyed");
                return;
            }

            IsShow = false;


            if (Root != null)
            {
                Root.SetActive(false);
            }
            OnHide();

        }

        public void Destroy()
        {
            if (IsDestroyed) return;
            IsDestroyed = true;
            if (Root != null)
            {
                GameObject.Destroy(Root);
            }
            OnDestroy();
        }

        protected virtual void OnShow()
        {
            if (Root == null) return;
            Root.SetActive(true);
        }
        protected virtual void OnHide()
        {
            if (Root == null) return;
            Root.SetActive(false);
        }

        public T Query<T>(string childName) where T : Component
        {
            if (!childrenDict.ContainsKey(childName))
            {
                Debug.LogWarning($"{childName} not found");
                return null;
            }

            Transform child = childrenDict[childName];
            T component = child.GetComponent<T>();
            if (component == null)
            {
                Debug.LogWarning($"{childName} not found component {typeof(T).Name}");
            }
            return component;
        }

        private void QueryChildWithCache(Transform parent)
        {
            if (parent == null) return;

            Queue<Transform> queue = new Queue<Transform>();
            queue.Enqueue(parent);

            while (queue.Count > 0)
            {
                Transform child = queue.Dequeue();
                if (!childrenDict.ContainsKey(child.name))
                {
                    childrenDict.Add(child.name, child);
                }

                for (int i = 0; i < child.childCount; i++)
                {
                    queue.Enqueue(child.GetChild(i));
                }
            }
        }

        protected abstract UniTask OnInit();
        protected abstract UniTask<GameObject> LoadView();
        protected abstract void BindComponent();
        protected abstract void OnDestroy();
    }
}
