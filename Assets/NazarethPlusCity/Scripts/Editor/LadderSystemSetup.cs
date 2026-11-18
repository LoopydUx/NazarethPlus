using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NazarethPlusCity.EditorTools
{
    public static class LadderSystemSetup
    {
        public static string ReportPath =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "LadderSystemReport.txt");

        [MenuItem("NazarethPlusCity/Setup Ladder System")]
        public static void InstallFromMenu()
        {
            Run();
            Debug.Log("Ladder system setup finished. See " + ReportPath);
        }

        public static void InstallBatch()
        {
            try
            {
                Run();
                Debug.Log("LADDER_SETUP_OK");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                File.WriteAllText(ReportPath, "FAIL\n" + exception);
                Debug.LogError("LADDER_SETUP_FAIL\n" + exception);
                EditorApplication.Exit(1);
            }
        }

        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Ladder setup cannot change the scene during Play mode.");

            var report = new StringBuilder();
            report.AppendLine("NazarethPlusCity ladder system setup");
            GameObject prefab = WoodenLadderPrefabBuilder.BuildOrUpdate();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            var settings = AssetDatabase.LoadAssetAtPath<LadderSystemSettings>(LadderIds.SettingsPath);
            if (settings == null)
                throw new InvalidOperationException("LadderSystemSettings was not created at " + LadderIds.SettingsPath + ".");
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LadderIds.PrefabPath);
            if (prefab == null)
                throw new InvalidOperationException("Ladder section prefab was not created at " + LadderIds.PrefabPath + ".");
            var section = prefab.GetComponent<LadderSection>();
            if (section == null)
                throw new InvalidOperationException("Ladder section component is missing on the prefab.");

            report.AppendLine("Source mesh height: " + settings.sourceMeshHeightMeters.ToString("0.000") + " m");
            report.AppendLine("Dense rung scale: " + settings.denseRungScale.ToString("0.00"));
            report.AppendLine("Section height (dense): " + settings.sectionHeightMeters.ToString("0.000") + " m");
            report.AppendLine("Section width: " + settings.sectionWidthMeters.ToString("0.000") + " m");
            report.AppendLine("Section thickness: " + settings.sectionThicknessMeters.ToString("0.000") + " m");
            report.AppendLine("Wood per section: " + settings.woodPerSection);
            report.AppendLine("Starting coins: " + settings.startingCoins);

            var scene = EditorSceneManager.GetActiveScene();
            string openPath = string.IsNullOrEmpty(scene.path) ? "" : scene.path.Replace('\\', '/');
            if (openPath != CityImporter.ScenePath)
                scene = EditorSceneManager.OpenScene(CityImporter.ScenePath, OpenSceneMode.Single);

            var walker = UnityEngine.Object.FindAnyObjectByType<FirstPersonWalker>();
            if (walker == null)
                throw new InvalidOperationException("CityWalker is missing.");

            EnsurePlayerWiring(walker, settings, prefab);
            float average = PlayerResources.AverageClimbHeight();
            int sections = LadderMath.RequiredSections(average, settings.sectionHeightMeters);
            int wood = LadderMath.StartingWood(average, settings.sectionHeightMeters, settings.woodPerSection, settings.starterLadderBudget);
            report.AppendLine("Average climb height (ActiveHeight, including overrides): " + average.ToString("0.000") + " m");
            report.AppendLine("Sections per average ladder: " + sections);
            report.AppendLine("Starting wood = " + settings.starterLadderBudget + " × " + sections + " × " + settings.woodPerSection + " = " + wood);
            report.AppendLine("Example short final section: climb 9.00 m / section " + settings.sectionHeightMeters.ToString("0.000")
                + " m => " + LadderMath.RequiredSections(9f, settings.sectionHeightMeters) + " sections, last uses "
                + LadderMath.SectionUseHeight(LadderMath.RequiredSections(9f, settings.sectionHeightMeters) - 1,
                    LadderMath.RequiredSections(9f, settings.sectionHeightMeters), 9f, settings.sectionHeightMeters).ToString("0.000") + " m.");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            settings = AssetDatabase.LoadAssetAtPath<LadderSystemSettings>(LadderIds.SettingsPath);
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LadderIds.PrefabPath);
            Verify(walker, settings, prefab, report);
            File.WriteAllText(ReportPath, report.ToString());
            Debug.Log(report.ToString());
        }

        public static void EnsurePlayerWiring(FirstPersonWalker walker, LadderSystemSettings settings, GameObject prefab)
        {
            if (walker == null)
                return;
            var hud = walker.transform.Find("WalkHud");
            if (hud != null)
                LadderHud.Ensure(hud);

            var resources = walker.GetComponent<PlayerResources>() ?? walker.gameObject.AddComponent<PlayerResources>();
            var builder = walker.GetComponent<LadderBuilder>() ?? walker.gameObject.AddComponent<LadderBuilder>();
            var climb = walker.GetComponent<LadderClimbController>() ?? walker.gameObject.AddComponent<LadderClimbController>();
            builder.settings = settings;
            builder.sectionPrefab = prefab;
            builder.resources = resources;
            climb.settings = settings;
            climb.builder = builder;
            EditorUtility.SetDirty(walker.gameObject);
            EditorUtility.SetDirty(builder);
            EditorUtility.SetDirty(climb);
            EditorUtility.SetDirty(resources);
        }

        static void Verify(FirstPersonWalker walker, LadderSystemSettings settings, GameObject prefab, StringBuilder report)
        {
            var failures = new List<string>();
            if (settings == null)
                settings = AssetDatabase.LoadAssetAtPath<LadderSystemSettings>(LadderIds.SettingsPath);
            if (prefab == null)
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LadderIds.PrefabPath);
            Check(failures, prefab != null, "section prefab exists");
            Check(failures, settings != null, "settings asset exists");
            Check(failures, walker.GetComponent<LadderBuilder>() != null, "LadderBuilder on CityWalker");
            Check(failures, walker.GetComponent<LadderClimbController>() != null, "LadderClimbController on CityWalker");
            Check(failures, walker.GetComponent<PlayerResources>() != null, "PlayerResources on CityWalker");
            var builder = walker.GetComponent<LadderBuilder>();
            Check(failures, builder != null && builder.sectionPrefab != null, "builder prefab assigned");
            Check(failures, builder != null && builder.settings != null, "builder settings assigned");
            Check(failures, walker.transform.Find("WalkHud/" + LadderHud.ResourceName) != null, "resource HUD");
            Check(failures, walker.transform.Find("WalkHud/" + LadderHud.ProgressName) != null, "progress HUD");
            LadderSection section = prefab != null ? prefab.GetComponent<LadderSection>() : null;
            Check(failures, section != null && section.bottomSnap != null && section.topSnap != null, "snap points");
            if (settings != null && section != null)
                Check(failures, Mathf.Abs(settings.sectionHeightMeters - section.sectionHeight) < 0.01f, "settings match prefab height");
            if (settings != null)
            {
                float average = PlayerResources.AverageClimbHeight();
                int wood = LadderMath.StartingWood(average, settings.sectionHeightMeters, settings.woodPerSection, settings.starterLadderBudget);
                Check(failures, wood > 0, "starting wood > 0 (" + wood + ")");
                report.AppendLine("Verify average climb " + average.ToString("0.000") + " m => starting wood " + wood);
                int need = LadderMath.RequiredSections(9f, settings.sectionHeightMeters);
                float last = LadderMath.SectionUseHeight(need - 1, need, 9f, settings.sectionHeightMeters);
            Check(failures, need >= 8, "9 m roof needs many dense sections (" + need + ")");
            Check(failures, last < settings.sectionHeightMeters - 0.01f, "9 m roof uses a shorter final section (" + last.ToString("0.000") + " m)");
            Check(failures, settings.denseRungScale < 0.5f && settings.sectionHeightMeters < 1.2f, "dense rung section height under 1.2 m");
            }

            if (failures.Count == 0)
            {
                report.AppendLine("VERIFY_OK");
            }
            else
            {
                report.AppendLine("VERIFY_FAIL");
                for (int i = 0; i < failures.Count; i++)
                    report.AppendLine(" - " + failures[i]);
                throw new InvalidOperationException("Ladder setup verification failed:\n" + string.Join("\n", failures));
            }
        }

        static void Check(List<string> failures, bool ok, string label)
        {
            if (!ok)
                failures.Add(label);
        }
    }
}
