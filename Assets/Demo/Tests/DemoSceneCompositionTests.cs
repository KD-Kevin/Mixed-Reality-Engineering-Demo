using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace EngineeringDemo.Tests
{
    public sealed class DemoSceneCompositionTests
    {
        [TestCase("FridgeDemo", false, -82f)]
        [TestCase("HandDemo", true, 60f)]
        public void SceneKeepsItsSettingsAndWiresItsInteraction(string sceneName, bool mirrored, float leftHandHue)
        {
            var path = "Assets/Demo/" + sceneName + ".unity";
            var scene = SceneManager.GetSceneByPath(path);
            bool openedForTest = !scene.isLoaded;
            if (openedForTest)
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);

            try
            {
                var view = FindComponent<HandDemoView>(scene);
                Assert.That(view, Is.Not.Null);
                Assert.That(view.Tracking, Is.SameAs(FindComponent<HandManager>(scene)));
                Assert.That(view.ViewCamera, Is.Not.Null);
                Assert.That(view.Visuals, Is.SameAs(view.GetComponent<HandDemoVisuals>()));

                var tracking = ReadReference<HandTrackingSettings>(view.Tracking, "_settings");
                Assert.That(tracking.Model, Is.Not.Null);
                Assert.That(tracking.MirrorWebcam, Is.EqualTo(mirrored));
                Assert.That(tracking.GestureChangeSeconds, Is.EqualTo(mirrored ? 0.12f : 0f));
                Assert.That(tracking.InitialWristDistance, Is.EqualTo(0.6f));
                var visuals = ReadReference<HandVisualSettings>(view.Visuals, "_settings");
                Assert.That(visuals.LeftHandHue, Is.EqualTo(leftHandHue));

                if (sceneName == "FridgeDemo")
                    AssertFridgeSetup(scene, view);
                else
                    AssertTargetPracticeSetup(view);
            }
            finally
            {
                if (openedForTest)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void AssertFridgeSetup(Scene scene, HandDemoView view)
        {
            var interaction = ReadReference<FridgeDemoInteraction>(view, "_fridgeInteraction");
            var settings = ReadReference<FridgeInteractionSettings>(interaction, "_settings");
            Assert.That(settings.TriggerReadyExtension, Is.EqualTo(0.3f));
            Assert.That(settings.FireThreshold, Is.EqualTo(0.2f));
            Assert.That(ReadReference<FridgeDetector>(interaction, "_fridgeDetector"), Is.SameAs(FindComponent<FridgeDetector>(scene)));
            var hologram = ReadReference<FridgeHologram>(interaction, "_fridgeHologram");
            Assert.That(ReadReference<GameObject>(hologram, "_modelPrefab"), Is.Not.Null);

            var roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                var buttons = roots[i].GetComponentsInChildren<Button>(true);
                for (int j = 0; j < buttons.Length; j++)
                {
                    var click = buttons[j].onClick;
                    if (click.GetPersistentEventCount() == 0 || click.GetPersistentMethodName(0) != nameof(FridgeDemoInteraction.ShowTestHologram))
                        continue;

                    Assert.That(click.GetPersistentTarget(0), Is.SameAs(interaction));
                    return;
                }
            }
            Assert.Fail("The Test Hologram button is missing its persistent callback.");
        }

        private static void AssertTargetPracticeSetup(HandDemoView view)
        {
            var practice = ReadReference<HandTargetPractice>(view, "_targetPractice");
            var targets = new SerializedObject(practice).FindProperty("_targets");
            Assert.That(targets.arraySize, Is.EqualTo(3));
            for (int i = 0; i < targets.arraySize; i++)
                Assert.That(targets.GetArrayElementAtIndex(i).objectReferenceValue, Is.Not.Null);
        }

        private static T ReadReference<T>(Object owner, string field) where T : Object
        {
            var reference = new SerializedObject(owner).FindProperty(field).objectReferenceValue as T;
            Assert.That(reference, Is.Not.Null, $"{owner.name}.{field}");
            return reference;
        }

        private static T FindComponent<T>(Scene scene) where T : Component
        {
            var roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                var component = roots[i].GetComponentInChildren<T>(true);
                if (component != null)
                    return component;
            }
            return null;
        }
    }
}
