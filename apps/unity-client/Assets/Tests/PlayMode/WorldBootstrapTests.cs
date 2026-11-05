using System.Collections;
using NUnit.Framework;
using Orbis.Bootstrap;
using Orbis.World;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Orbis.Tests.PlayMode
{
    public sealed class WorldBootstrapTests
    {
        [UnityTest]
        public IEnumerator BootstrapCreatesGlobeCameraAndHud()
        {
            var host = new GameObject("Bootstrap Test Host");
            host.AddComponent<WorldBootstrap>();
            yield return null;

            var globe = Object.FindAnyObjectByType<GlobeRenderer>();
            var cameraController = Object.FindAnyObjectByType<GeoCameraController>();
            var document = Object.FindAnyObjectByType<UIDocument>();

            Assert.That(globe, Is.Not.Null);
            Assert.That(globe.SurfaceCollider, Is.Not.Null);
            Assert.That(cameraController, Is.Not.Null);
            Assert.That(document, Is.Not.Null);
            Assert.That(document.rootVisualElement.Q<Button>("style-toggle"), Is.Not.Null);

            Object.Destroy(host);
            yield return null;
        }

        [Test]
        public void HudInputGateCanReleaseCameraInput()
        {
            HudInputGate.Reset();
            HudInputGate.Enter();
            Assert.That(HudInputGate.IsPointerBlocked, Is.True);
            HudInputGate.Leave();
            Assert.That(HudInputGate.IsPointerBlocked, Is.False);
        }
    }
}
