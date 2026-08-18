using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace Mochi
{

    /// <summary>
    /// 适用于MonoBehaviour的单例类
    /// </summary>
    /// <typeparam name="T">派生类型</typeparam> 
    public abstract class MonoSingleton<T> : MonoBehaviour where T : MonoSingleton<T>
    {
        protected static T instance;
        private static readonly object locker = new object();

        public static T Instance
        {
            get
            {
                if (instance == null)
                {
                    ForcedInstance();
                }

                return instance;
            }

            protected set
            {
                if (instance != value)
                {
                    Unload();
                }

                instance = value;
                DontDestroyOnLoad(value);
            }
        }

        public static T ForcedInstance()
        {
            lock (locker)
            {
                GameObject go = new GameObject();
                instance = go.AddComponent<T>();
                instance.Init();
                go.name = typeof(T).Name;
                DontDestroyOnLoad(go);
            }

            return instance;
        }

        /// <summary>
        /// 卸载单例类
        /// </summary>
        public static void Unload()
        {
            if (instance == null) return;

            lock (locker)
            {
                Destroy(instance.gameObject);
                instance = null;
            }
        }

        /// <summary>
        /// 初始化单例类
        /// </summary>
        protected abstract void Init();
    }
}

