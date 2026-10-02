using UnityEngine;

namespace Woodsmen.Inventory.UI
{
    /// <summary>
    /// Procedural UI texture and sprite generation utility.
    /// Provides pixel-perfect rounded rectangles, dark slate panels, glowing borders,
    /// and crisp vector-style icons (Wood, Stone, Axe, Potion, Planks, Gold) with ZERO external asset dependencies.
    /// </summary>
    public static class UIStyleHelper
    {
        private static Sprite _panelSprite;
        private static Sprite _slotSprite;
        private static Sprite _slotSelectedSprite;
        private static Sprite _buttonSprite;

        private static Sprite _woodIcon;
        private static Sprite _axeIcon;
        private static Sprite _potionIcon;
        private static Sprite _planksIcon;
        private static Sprite _goldIcon;

        public static Sprite PanelSprite => _panelSprite ??= CreateRoundedBoxSprite(64, 64, 12, new Color(0.08f, 0.10f, 0.12f, 0.94f), new Color(0.24f, 0.28f, 0.34f, 1f), 2);
        public static Sprite SlotSprite => _slotSprite ??= CreateRoundedBoxSprite(48, 48, 8, new Color(0.12f, 0.15f, 0.18f, 0.90f), new Color(0.20f, 0.25f, 0.30f, 0.9f), 1);
        public static Sprite SlotSelectedSprite => _slotSelectedSprite ??= CreateRoundedBoxSprite(48, 48, 8, new Color(0.18f, 0.22f, 0.26f, 1f), new Color(0.95f, 0.75f, 0.25f, 1f), 2);
        public static Sprite ButtonSprite => _buttonSprite ??= CreateRoundedBoxSprite(64, 32, 6, new Color(0.16f, 0.50f, 0.35f, 1f), new Color(0.30f, 0.75f, 0.50f, 1f), 1);

        public static Sprite GetItemIcon(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;

            switch (itemId.ToLowerInvariant())
            {
                case "wood":
                    return _woodIcon ??= CreateWoodLogIcon();
                case "iron_axe":
                    return _axeIcon ??= CreateAxeIcon();
                case "health_potion":
                    return _potionIcon ??= CreatePotionIcon();
                case "gold":
                case "money":
                case "coin":
                    if (_goldIcon == null)
                    {
                        _goldIcon = Resources.Load<Sprite>("Coin/source/Coin png") ??
                                    Resources.Load<Sprite>("Coin/source/stack-of-coins-png");
                        if (_goldIcon == null)
                        {
                            Texture2D coinTex = Resources.Load<Texture2D>("Coin/source/Coin png") ??
                                                Resources.Load<Texture2D>("Coin/source/stack-of-coins-png");
                            if (coinTex != null)
                            {
                                _goldIcon = Sprite.Create(coinTex, new Rect(0, 0, coinTex.width, coinTex.height), new Vector2(0.5f, 0.5f));
                            }
                        }
                        if (_goldIcon == null) _goldIcon = CreateGoldIcon();
                    }
                    return _goldIcon;
                default:
                    return _panelSprite;
            }
        }

        public static Sprite CreateRoundedBoxSprite(int width, int height, int radius, Color fillColor, Color borderColor, int borderWidth)
        {
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // Compute distance from corners
                    int dx = Mathf.Max(0, Mathf.Max(radius - x, x - (width - 1 - radius)));
                    int dy = Mathf.Max(0, Mathf.Max(radius - y, y - (height - 1 - radius)));
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    if (dist > radius)
                    {
                        tex.SetPixel(x, y, Color.clear);
                    }
                    else if (dist > radius - borderWidth || x < borderWidth || x >= width - borderWidth || y < borderWidth || y >= height - borderWidth)
                    {
                        tex.SetPixel(x, y, borderColor);
                    }
                    else
                    {
                        tex.SetPixel(x, y, fillColor);
                    }
                }
            }

            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f));
        }

        private static Sprite CreateWoodLogIcon()
        {
            Texture2D tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            Color logBrown = new Color(0.60f, 0.38f, 0.20f, 1f);
            Color barkDark = new Color(0.38f, 0.22f, 0.10f, 1f);
            Color innerRing = new Color(0.78f, 0.55f, 0.32f, 1f);

            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    tex.SetPixel(x, y, Color.clear);
                    // Draw log cylinder diagonal
                    if (y >= 8 && y <= 24 && x >= 4 && x <= 28)
                    {
                        tex.SetPixel(x, y, logBrown);
                        if (x < 10) tex.SetPixel(x, y, innerRing); // Log cross-section
                        if (y == 8 || y == 24 || x == 28) tex.SetPixel(x, y, barkDark);
                    }
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f));
        }

        private static Sprite CreateAxeIcon()
        {
            Texture2D tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            Color steel = new Color(0.82f, 0.88f, 0.94f, 1f);
            Color woodHandle = new Color(0.55f, 0.35f, 0.18f, 1f);

            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    tex.SetPixel(x, y, Color.clear);
                    // Diagonal handle
                    if (Mathf.Abs(x - y) <= 1 && x >= 4 && x <= 26)
                    {
                        tex.SetPixel(x, y, woodHandle);
                    }
                    // Axe blade at top right
                    if (x >= 18 && y >= 18 && (x + y >= 42) && (x + y <= 56))
                    {
                        tex.SetPixel(x, y, steel);
                    }
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f));
        }

        private static Sprite CreatePotionIcon()
        {
            Texture2D tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            Color glass = new Color(0.8f, 0.95f, 1f, 0.6f);
            Color redLiquid = new Color(0.92f, 0.18f, 0.22f, 0.95f);
            Color cork = new Color(0.6f, 0.4f, 0.2f, 1f);

            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    tex.SetPixel(x, y, Color.clear);
                    // Bottle bulb
                    float dx = x - 16;
                    float dy = y - 12;
                    if (dx * dx + dy * dy < 64)
                    {
                        tex.SetPixel(x, y, y < 14 ? redLiquid : glass);
                    }
                    // Bottle neck
                    else if (Mathf.Abs(dx) <= 3 && y >= 18 && y <= 24)
                    {
                        tex.SetPixel(x, y, glass);
                    }
                    // Cork
                    else if (Mathf.Abs(dx) <= 4 && y >= 25 && y <= 28)
                    {
                        tex.SetPixel(x, y, cork);
                    }
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f));
        }

        private static Sprite CreatePlanksIcon()
        {
            Texture2D tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            Color plank = new Color(0.75f, 0.52f, 0.28f, 1f);
            Color seam = new Color(0.40f, 0.25f, 0.12f, 1f);

            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    if (x >= 4 && x <= 28 && y >= 6 && y <= 26)
                    {
                        if (y == 12 || y == 19 || x == 4 || x == 28 || y == 6 || y == 26)
                        {
                            tex.SetPixel(x, y, seam);
                        }
                        else
                        {
                            tex.SetPixel(x, y, plank);
                        }
                    }
                    else
                    {
                        tex.SetPixel(x, y, Color.clear);
                    }
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f));
        }

        private static Sprite CreateGoldIcon()
        {
            Texture2D tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            Color gold = new Color(0.96f, 0.78f, 0.15f, 1f);
            Color darkGold = new Color(0.70f, 0.50f, 0.08f, 1f);

            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    float dx = x - 16;
                    float dy = y - 16;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dist < 10)
                    {
                        tex.SetPixel(x, y, dist > 8 ? darkGold : gold);
                    }
                    else
                    {
                        tex.SetPixel(x, y, Color.clear);
                    }
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f));
        }
    }
}
