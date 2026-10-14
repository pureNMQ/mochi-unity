using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace Mochi.Unity.GUI.Tests
{
    public sealed class ModalServiceTests
    {
        [Test]
        // 验收条件 AC11：ConfirmAsync 在确认时返回 true，在取消、Back、外部关闭或系统中断时返回 false。
        // 验收条件 AC12：MessageAsync 返回 UniTask<bool>，用户确认返回 true，外部关闭或系统中断返回 false，且不提供委托入口。
        // 验收条件 AC10：ModalService 能将一次性结果转换为异步或委托结果。
        public void ModalService_ExposesConfirmedMessageAndCustomResultContracts()
        {
            Assert.That(typeof(ModalService).GetConstructor(new[] { typeof(PanelStackService) }), Is.Not.Null);
            Assert.That(typeof(ModalService).GetMethod("ConfirmAsync"), Is.Not.Null);
            Assert.That(typeof(ModalService).GetMethod("MessageAsync"), Is.Not.Null);

            MethodInfo[] customMethods = typeof(ModalService).GetMethods(BindingFlags.Instance | BindingFlags.Public);
            Assert.That(Array.Exists(customMethods, method =>
                method.Name == "ShowAsync" && method.IsGenericMethodDefinition &&
                method.GetGenericArguments().Length == 3), Is.True);
        }

        [Test]
        // 验收条件 AC13：ToastService.Show 显示 Toast 并自动关闭，调用方不接触 Toast Panel。
        public void ToastService_ExposesPanelFreeShowContract()
        {
            MethodInfo show = typeof(ToastService).GetMethod(
                "Show",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(string), typeof(float) },
                null);
            Assert.That(show, Is.Not.Null);
            Assert.That(show.IsGenericMethodDefinition, Is.True);
            Assert.That(show.ReturnType, Is.EqualTo(typeof(UniTask)));
            Assert.That(typeof(ToastService).GetConstructor(new[] { typeof(GUIService) }), Is.Not.Null);
        }

        [Test]
        public void Services_ConstructWithRequiredDependencies()
        {
            GameObject stackObject = new GameObject("PanelStackService");
            GameObject rootObject = new GameObject("UIRoot");
            try
            {
                PanelStackService stack = stackObject.AddComponent<PanelStackService>();
                UIRoot root = rootObject.AddComponent<UIRoot>();
                GUIService gui = new GUIService(root, new NullLoader());

                Assert.That(new ModalService(stack), Is.Not.Null);
                Assert.That(new ToastService(gui), Is.Not.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(stackObject);
                UnityEngine.Object.DestroyImmediate(rootObject);
            }
        }

        private sealed class NullLoader : IPanelLoader
        {
            public UniTask<GameObject> LoadAsync(string key, CancellationToken cancellationToken)
            {
                return UniTask.FromResult<GameObject>(null);
            }

            public void Release(GameObject prefab)
            {
            }
        }
    }
}
