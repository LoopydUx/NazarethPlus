using UnityEngine;
using UnityEngine.UI;

namespace NazarethPlusCity
{
    /// <summary>
    /// Keeps the welcome card row readable on a desktop window and on a narrow touch layout.
    /// </summary>
    public class WelcomeWindowLayout : MonoBehaviour
    {
        public RectTransform panel;
        public RectTransform cards;
        public GridLayoutGroup grid;
        public float maxWidth = 1100f;
        public float wideHeight = 640f;
        public float stackedHeight = 760f;
        public float margin = 28f;
        public float stackBelowWidth = 780f;
        public float minimumCardHeight = 112f;

        void OnEnable()
        {
            Apply();
        }

        void OnRectTransformDimensionsChange()
        {
            Apply();
        }

        public void Apply()
        {
            if (panel == null)
                return;
            var root = (RectTransform)transform;
            float width = root.rect.width;
            float height = root.rect.height;
            if (width < 8f || height < 8f)
                return;

            float panelWidth = Mathf.Clamp(width - margin * 2f, 300f, maxWidth);
            bool stack = panelWidth < stackBelowWidth;
            float preferred = stack ? stackedHeight : wideHeight;
            float panelHeight = Mathf.Clamp(preferred, 420f, Mathf.Max(420f, height - margin * 2f));
            panel.sizeDelta = new Vector2(panelWidth, panelHeight);
            if (grid == null || cards == null)
                return;

            Canvas.ForceUpdateCanvases();
            float cardsWidth = cards.rect.width;
            float cardsHeight = cards.rect.height;
            if (cardsWidth < 8f)
                cardsWidth = panelWidth - 48f;
            if (cardsHeight < 8f)
                cardsHeight = panelHeight - 188f;
            int columns = stack ? 1 : 3;
            int rows = stack ? 3 : 1;
            float spacing = grid.spacing.x > 1f ? grid.spacing.x : 16f;
            float cellWidth = (cardsWidth - spacing * (columns - 1)) / columns;
            float cellHeight = (cardsHeight - spacing * (rows - 1)) / rows;
            cellWidth = Mathf.Max(120f, cellWidth);
            cellHeight = Mathf.Clamp(cellHeight, minimumCardHeight, 280f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            grid.cellSize = new Vector2(cellWidth, cellHeight);
        }
    }
}
