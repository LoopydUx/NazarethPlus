using UnityEngine;
using UnityEngine.UI;

namespace NazarethPlusCity
{
    /// <summary>
    /// Gameplay HUD for choosing and confirming a placeable object.
    /// </summary>
    public static class PlacementHudBuilder
    {
        public const string AddButtonName = "AddAssetButton";
        public const string CatalogName = "CatalogPanel";
        public const string InstructionName = "InstructionPanel";
        public const string ManageName = "ManagePanel";
        public const string LoadStatusName = "LoadStatus";
        public const string CatalogListName = "CatalogList";

        static Sprite _white;

        public static void Ensure(Transform hud, bool rebuild)
        {
            if (hud == null)
                return;
            if (rebuild)
            {
                DestroyNamed(hud, AddButtonName);
                DestroyNamed(hud, CatalogName);
                DestroyNamed(hud, InstructionName);
                DestroyNamed(hud, ManageName);
                DestroyNamed(hud, LoadStatusName);
            }

            if (hud.Find(AddButtonName) == null)
                Build(hud);
            var hint = hud.Find("Hint");
            if (hint != null)
            {
                var text = hint.GetComponent<Text>();
                if (text != null)
                    text.raycastTarget = false;
            }
        }

        public static Button CreateChoice(Transform parent, Sprite thumbnail, string label, bool interactable)
        {
            var row = CreateImage("Choice", parent, White(), new Color(0.22f, 0.18f, 0.15f, 0.98f));
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 8, 8);
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            var element = row.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = 76f;
            element.minHeight = 76f;
            var button = row.gameObject.AddComponent<Button>();
            button.targetGraphic = row;
            button.interactable = interactable;
            var colors = button.colors;
            colors.normalColor = new Color(0.22f, 0.18f, 0.15f, 0.98f);
            colors.highlightedColor = new Color(0.40f, 0.33f, 0.24f, 1f);
            colors.pressedColor = new Color(0.55f, 0.44f, 0.28f, 1f);
            colors.disabledColor = new Color(0.16f, 0.14f, 0.13f, 0.7f);
            colors.fadeDuration = 0.05f;
            button.colors = colors;
            var navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
            var icon = CreateImage("Thumbnail", row.transform, thumbnail != null ? thumbnail : White(), Color.white);
            var iconElement = icon.gameObject.AddComponent<LayoutElement>();
            iconElement.preferredWidth = 60f;
            iconElement.preferredHeight = 60f;
            icon.preserveAspect = true;
            var text = CreateText("Label", row.transform, BuiltinFont(), label, 22, FontStyle.Bold, new Color(1f, 0.98f, 0.94f, 1f), TextAnchor.MiddleLeft);
            var textElement = text.gameObject.AddComponent<LayoutElement>();
            textElement.preferredWidth = 280f;
            textElement.flexibleWidth = 1f;
            return button;
        }

        static void Build(Transform hud)
        {
            Font font = BuiltinFont();
            Sprite sprite = White();
            var add = CreateButton(hud, AddButtonName, "+ Add Asset", font, sprite, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(210f, 48f));
            add.GetComponent<Image>().color = new Color(0.12f, 0.16f, 0.12f, 0.9f);

            var status = CreateText(LoadStatusName, hud, font, "", 18, FontStyle.Normal, new Color(1f, 0.86f, 0.55f, 1f), TextAnchor.UpperLeft);
            var statusRect = status.rectTransform;
            statusRect.anchorMin = new Vector2(0f, 1f);
            statusRect.anchorMax = new Vector2(0f, 1f);
            statusRect.pivot = new Vector2(0f, 1f);
            statusRect.anchoredPosition = new Vector2(24f, -84f);
            statusRect.sizeDelta = new Vector2(760f, 120f);
            status.raycastTarget = false;
            status.gameObject.SetActive(false);

            var catalog = CreatePanel(hud, CatalogName, new Vector2(460f, 520f));
            CreateText("Title", catalog, font, "Add asset", 28, FontStyle.Bold, new Color(1f, 0.98f, 0.94f, 1f), TextAnchor.MiddleCenter);
            var title = catalog.Find("Title").GetComponent<RectTransform>();
            title.anchorMin = new Vector2(0f, 1f);
            title.anchorMax = new Vector2(1f, 1f);
            title.pivot = new Vector2(0.5f, 1f);
            title.anchoredPosition = new Vector2(0f, -16f);
            title.sizeDelta = new Vector2(-32f, 40f);
            var message = CreateText("Message", catalog, font, "", 16, FontStyle.Normal, new Color(1f, 0.55f, 0.45f, 1f), TextAnchor.UpperLeft);
            var messageRect = message.rectTransform;
            messageRect.anchorMin = new Vector2(0f, 1f);
            messageRect.anchorMax = new Vector2(1f, 1f);
            messageRect.pivot = new Vector2(0.5f, 1f);
            messageRect.anchoredPosition = new Vector2(0f, -60f);
            messageRect.sizeDelta = new Vector2(-36f, 72f);
            message.raycastTarget = false;
            var scroll = CreateScroll(catalog, CatalogListName);
            var scrollRect = scroll.GetComponent<RectTransform>();
            scrollRect.anchorMin = new Vector2(0f, 0f);
            scrollRect.anchorMax = new Vector2(1f, 1f);
            scrollRect.offsetMin = new Vector2(16f, 76f);
            scrollRect.offsetMax = new Vector2(-16f, -140f);
            var close = CreateButton(catalog, "CloseCatalogButton", "Close", font, sprite, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(180f, 42f));
            close.GetComponentInChildren<Text>().text = "Close";
            var note = CreateText("Note", catalog, font, "Benches, trees, and streetlights use this same catalog.", 14, FontStyle.Italic, new Color(0.85f, 0.8f, 0.72f, 0.9f), TextAnchor.MiddleCenter);
            var noteRect = note.rectTransform;
            noteRect.anchorMin = new Vector2(0f, 0f);
            noteRect.anchorMax = new Vector2(1f, 0f);
            noteRect.pivot = new Vector2(0.5f, 0f);
            noteRect.anchoredPosition = new Vector2(0f, 64f);
            noteRect.sizeDelta = new Vector2(-24f, 24f);
            note.raycastTarget = false;
            catalog.gameObject.SetActive(false);

            var instruction = CreatePanel(hud, InstructionName, new Vector2(760f, 168f));
            var instructionRect = instruction.GetComponent<RectTransform>();
            instructionRect.anchorMin = instructionRect.anchorMax = new Vector2(0.5f, 0f);
            instructionRect.pivot = new Vector2(0.5f, 0f);
            instructionRect.anchoredPosition = new Vector2(0f, 78f);
            var reason = CreateText("Reason", instruction, font, "Ready to place.", 22, FontStyle.Bold, new Color(0.65f, 1f, 0.7f, 1f), TextAnchor.MiddleCenter);
            var reasonRect = reason.rectTransform;
            reasonRect.anchorMin = new Vector2(0f, 1f);
            reasonRect.anchorMax = new Vector2(1f, 1f);
            reasonRect.pivot = new Vector2(0.5f, 1f);
            reasonRect.anchoredPosition = new Vector2(0f, -12f);
            reasonRect.sizeDelta = new Vector2(-24f, 36f);
            reason.raycastTarget = false;
            var help = CreateText("Help", instruction, font, "", 16, FontStyle.Normal, new Color(1f, 0.98f, 0.94f, 0.92f), TextAnchor.MiddleCenter);
            var helpRect = help.rectTransform;
            helpRect.anchorMin = new Vector2(0f, 1f);
            helpRect.anchorMax = new Vector2(1f, 1f);
            helpRect.pivot = new Vector2(0.5f, 1f);
            helpRect.anchoredPosition = new Vector2(0f, -48f);
            helpRect.sizeDelta = new Vector2(-24f, 48f);
            help.raycastTarget = false;
            CreateButton(instruction, "ConfirmButton", "Confirm", font, sprite, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(-12f, 16f), new Vector2(180f, 42f));
            CreateButton(instruction, "CancelButton", "Cancel", font, sprite, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 0f), new Vector2(12f, 16f), new Vector2(180f, 42f));
            instruction.gameObject.SetActive(false);

            var manage = CreatePanel(hud, ManageName, new Vector2(280f, 280f));
            var manageRect = manage.GetComponent<RectTransform>();
            manageRect.anchorMin = manageRect.anchorMax = new Vector2(1f, 0.5f);
            manageRect.pivot = new Vector2(1f, 0.5f);
            manageRect.anchoredPosition = new Vector2(-24f, 0f);
            var manageTitle = CreateText("Title", manage, font, "Bin", 24, FontStyle.Bold, new Color(1f, 0.98f, 0.94f, 1f), TextAnchor.MiddleCenter);
            var manageTitleRect = manageTitle.rectTransform;
            manageTitleRect.anchorMin = new Vector2(0f, 1f);
            manageTitleRect.anchorMax = new Vector2(1f, 1f);
            manageTitleRect.pivot = new Vector2(0.5f, 1f);
            manageTitleRect.anchoredPosition = new Vector2(0f, -12f);
            manageTitleRect.sizeDelta = new Vector2(-16f, 36f);
            CreateButton(manage, "MoveButton", "Move", font, sprite, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -64f), new Vector2(200f, 40f));
            CreateButton(manage, "RotateButton", "Rotate", font, sprite, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -112f), new Vector2(200f, 40f));
            CreateButton(manage, "DeleteButton", "Delete", font, sprite, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -160f), new Vector2(200f, 40f));
            CreateButton(manage, "CloseManageButton", "Close", font, sprite, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -216f), new Vector2(200f, 40f));
            manage.gameObject.SetActive(false);
        }

        static RectTransform CreateScroll(Transform parent, string contentName)
        {
            var scrollObject = new GameObject("CatalogScroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scrollObject.transform.SetParent(parent, false);
            var scrollImage = scrollObject.GetComponent<Image>();
            scrollImage.sprite = White();
            scrollImage.color = new Color(0f, 0f, 0f, 0.18f);
            var viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewportObject.transform.SetParent(scrollObject.transform, false);
            Stretch(viewportObject.GetComponent<RectTransform>());
            var viewportImage = viewportObject.GetComponent<Image>();
            viewportImage.sprite = White();
            viewportImage.color = new Color(1f, 1f, 1f, 0.01f);
            var content = new GameObject(contentName, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewportObject.transform, false);
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0f, 0f);
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = content.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = scrollObject.GetComponent<ScrollRect>();
            scroll.viewport = viewportObject.GetComponent<RectTransform>();
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            return scrollObject.GetComponent<RectTransform>();
        }

        static Transform CreatePanel(Transform parent, string name, Vector2 size)
        {
            var image = CreateImage(name, parent, White(), new Color(0.10f, 0.09f, 0.08f, 0.94f));
            var rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            return image.transform;
        }

        static Button CreateButton(Transform parent, string name, string label, Font font, Sprite sprite, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var image = CreateImage(name, parent, sprite, new Color(0.24f, 0.20f, 0.16f, 0.96f));
            var rect = image.rectTransform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
            var text = CreateText("Text", image.transform, font, label, 20, FontStyle.Bold, new Color(1f, 0.98f, 0.94f, 1f), TextAnchor.MiddleCenter);
            Stretch(text.rectTransform);
            return button;
        }

        static Image CreateImage(string name, Transform parent, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            return image;
        }

        static Text CreateText(string name, Transform parent, Font font, string value, int size, FontStyle style, Color color, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = font;
            text.text = value;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        static Font BuiltinFont()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return font;
        }

        static Sprite White()
        {
            if (_white != null)
                return _white;
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = Color.white;
            texture.SetPixels(pixels);
            texture.Apply();
            _white = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 4f);
            return _white;
        }

        static void DestroyNamed(Transform parent, string name)
        {
            var child = parent.Find(name);
            if (child == null)
                return;
            if (Application.isPlaying)
                Object.Destroy(child.gameObject);
            else
                Object.DestroyImmediate(child.gameObject);
        }
    }
}
