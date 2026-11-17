using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NazarethPlusCity.EditorTools
{
    public static class CityBuildMenu
    {
        const string ScenePath = "Assets/NazarethPlusCity/Scenes/NazarethPlusCity.unity";

        [MenuItem("NazarethPlusCity/Generate Or Rebuild Full City")]
        public static void GenerateFromMenu()
        {
            CityImporter.GenerateOrRebuild();
        }

        [MenuItem("NazarethPlusCity/Toggle Boundary Outline")]
        public static void ToggleOutline()
        {
            var outline = UnityEngine.Object.FindAnyObjectByType<BoundaryOutline>();
            if (outline == null)
            {
                Debug.LogWarning("Generate the city before toggling the boundary outline.");
                return;
            }

            outline.visible = !outline.visible;
            outline.ApplyVisibility();
            EditorUtility.SetDirty(outline);
            EditorSceneManager.MarkSceneDirty(outline.gameObject.scene);
        }

        public static void GenerateFullCityOnce()
        {
            try
            {
                CityImporter.GenerateOrRebuild();
                Debug.Log("NazarethPlusCity single generation succeeded.");
            }
            catch (Exception exception)
            {
                Debug.LogError("NazarethPlusCity generation failed: " + exception);
                EditorApplication.Exit(1);
            }
        }

        public static void GenerateFullCityBatch()
        {
            string reportPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "NazarethPlusCity_ImportReport.txt");
            try
            {
                CityImporter.GenerateOrRebuild();
                int buildings = UnityEngine.Object.FindObjectsByType<BuildingFeature>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
                int streets = UnityEngine.Object.FindObjectsByType<StreetFeature>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
                CityImporter.GenerateOrRebuild();
                int buildingsAgain = UnityEngine.Object.FindObjectsByType<BuildingFeature>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
                int streetsAgain = UnityEngine.Object.FindObjectsByType<StreetFeature>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
                string check = "Rebuild check: buildings " + buildings + " then " + buildingsAgain + "; streets " + streets + " then " + streetsAgain + ".";
                if (buildings != buildingsAgain || streets != streetsAgain)
                    throw new InvalidOperationException(check + " Counts changed, so the rebuild did not stay one-to-one.");
                File.AppendAllText(reportPath, Environment.NewLine + check + " No duplicate city objects were created." + Environment.NewLine);
                Debug.Log("NazarethPlusCity batch generation succeeded. " + check);
            }
            catch (Exception exception)
            {
                Debug.LogError("NazarethPlusCity generation failed: " + exception);
                EditorApplication.Exit(1);
            }
        }
    }

    [InitializeOnLoad]
    static class CityPlayModeStart
    {
        static CityPlayModeStart()
        {
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/NazarethPlusCity/Scenes/NazarethPlusCity.unity");
            if (scene != null)
                EditorSceneManager.playModeStartScene = scene;
        }
    }
}
