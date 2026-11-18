using UnityEngine;
using UnityEngine.UI;

namespace NazarethPlusCity
{
    public static class LadderHud
    {
        public const string ResourceName = "LadderResources";
        public const string ProgressName = "LadderProgress";

        static Sprite _white;

        public static void Ensure(Transform hud)
        {
            if (hud == null)
                return;
            if (hud.Find(ResourceName) == null)
                CreateResources(hud);
            if (hud.Find(ProgressName) == null)
                CreateProgress(hud);

            var hint = hud.Find("Hint");
            if (hint != null)
            {
                var text = hint.GetComponent<Text>();
                if (text != null && text.text != null && (text.text.Contains("E climb") || !text.text.Contains("F ladder")))
                    text.text = "WASD walk    Mouse look    Shift sprint    Space jump    F ladder    B add asset    Esc menu    R return to street";
            }
        }

        public static void Refresh(Transform hud, PlayerResources resources, LadderPlacement placement, int woodPerSection, bool climbing)
        {
            if (hud == null)
                return;
            Ensure(hud);
            var resource = hud.Find(ResourceName)?.GetComponent<Text>();
            if (resource != null && resources != null)
                resource.text = "Wood " + resources.Wood + "    Coins " + resources.Coins;

            var progress = hud.Find(ProgressName)?.GetComponent<Text>();
            if (progress == null)
                return;

            int built = placement.Existing != null ? placement.Existing.BuiltSections : 0;
            int required = Mathf.Max(1, placement.RequiredSections);

            if (climbing)
            {
                progress.gameObject.SetActive(true);
                progress.color = new Color(0.92f, 0.96f, 1f, 0.95f);
                if (placement.Valid && placement.Existing != null)
                {
                    progress.text = "Climbing · " + built + " / " + required + " sections · F add · W/S move";
                    if (built >= required)
                        progress.text = "Climbing · complete · W onto roof · S down";
                }
                else
                    progress.text = "Climbing · W/S move · F add section";
                return;
            }

            if (!placement.Valid && string.IsNullOrEmpty(placement.Reason))
            {
                progress.gameObject.SetActive(false);
                return;
            }

            progress.gameObject.SetActive(true);
            if (!placement.Valid)
            {
                progress.color = new Color(1f, 0.78f, 0.55f, 0.95f);
                progress.text = placement.Reason;
                return;
            }

            progress.color = new Color(0.92f, 0.98f, 0.90f, 0.95f);
            progress.text = built + " / " + required + " sections · " + woodPerSection + " wood per section";
            if (built >= required)
                progress.text = "Ladder complete · walk close to climb";
        }

        static void CreateResources(Transform hud)
        {
            var text = CreateText(ResourceName, hud, "Wood 0    Coins 0", 22, FontStyle.Bold, new Color(1f, 0.96f, 0.88f, 0.95f), TextAnchor.UpperRight);
            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-24f, -24f);
            rect.sizeDelta = new Vector2(420f, 40f);
            text.raycastTarget = false;
            text.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.7f);
        }

        static void CreateProgress(Transform hud)
        {
            var text = CreateText(ProgressName, hud, "", 20, FontStyle.Normal, new Color(0.92f, 0.98f, 0.90f, 0.95f), TextAnchor.LowerCenter);
            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 72f);
            rect.sizeDelta = new Vector2(900f, 36f);
            text.raycastTarget = false;
            text.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.7f);
            text.gameObject.SetActive(false);
        }

        static Text CreateText(string name, Transform parent, string value, int size, FontStyle style, Color color, TextAnchor anchor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = anchor;
            text.text = value;
            return text;
        }
    }
}
