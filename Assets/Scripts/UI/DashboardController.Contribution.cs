using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Aegis
{
    // Contribution Graph panel: one row per event type, one column per 5 s slice.
    public partial class DashboardController
    {
        const int ContribColumns = 44;
        const int ContribRows = 7;
        const int ContribCell = 8;
        const float ContribSliceSeconds = 5f;

        // Matches the demo's event mix so seeded history looks like live history.
        static readonly float[] RowWeights = { 1f, 0.45f, 0.25f, 0.3f, 0.18f, 0.12f, 0.3f };

        Label contribTotal;
        VisualElement contrib;
        readonly float[] contribCells = new float[ContribColumns * ContribRows];
        readonly float[] contribPeak = new float[ContribRows];
        Texture2D contribTexture;
        Color32[] contribPixels;
        float contribTimer;

        void CountContribution(GitEvent gitEvent)
        {
            int row = string.IsNullOrEmpty(gitEvent.Category)
                ? Mathf.Clamp((int)gitEvent.Type, 0, ContribRows - 1)
                : Catalog.StableHash(gitEvent.Category) % ContribRows;
            contribCells[(ContribColumns - 1) * ContribRows + row] += 1f;
        }

        void BuildContrib()
        {
            for (int c = 0; c < ContribColumns; c++)
            {
                for (int r = 0; r < ContribRows; r++)
                {
                    float wave = Mathf.PerlinNoise(c * 0.21f, r * 0.53f);
                    contribCells[c * ContribRows + r] = seeded ? wave * wave * 40f * RowWeight(r) : 0f;
                }
            }
            for (int r = 0; r < ContribRows; r++)
                contribPeak[r] = seeded ? 30f * RowWeight(r) : 1f;

            int width = ContribColumns * ContribCell;
            int height = ContribRows * ContribCell;
            contribPixels = new Color32[width * height];
            contribTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            if (contrib != null)
                contrib.style.backgroundImage = new StyleBackground(contribTexture);
            PaintContrib();
        }

        static float RowWeight(int row)
        {
            return RowWeights[Mathf.Clamp(row, 0, RowWeights.Length - 1)];
        }

        void TickContrib(float dt)
        {
            contribTimer += dt;
            if (contribTimer >= ContribSliceSeconds)
            {
                contribTimer -= ContribSliceSeconds;
                Array.Copy(contribCells, ContribRows, contribCells, 0, (ContribColumns - 1) * ContribRows);
                for (int r = 0; r < ContribRows; r++)
                    contribCells[(ContribColumns - 1) * ContribRows + r] = 0f;
            }
            if (Time.frameCount % 6 == 0)
                PaintContrib();
        }

        void PaintContrib()
        {
            if (contribTexture == null)
                return;
            for (int r = 0; r < ContribRows; r++)
            {
                float peak = 1f;
                for (int c = 0; c < ContribColumns - 1; c++)
                    peak = Mathf.Max(peak, contribCells[c * ContribRows + r]);
                contribPeak[r] = Mathf.Lerp(contribPeak[r], peak, 0.1f);
            }

            int width = ContribColumns * ContribCell;
            var empty = new Color32(0, 0, 0, 0);
            for (int i = 0; i < contribPixels.Length; i++)
                contribPixels[i] = empty;

            float total = 0f;
            for (int c = 0; c < ContribColumns; c++)
            {
                for (int r = 0; r < ContribRows; r++)
                {
                    float value = contribCells[c * ContribRows + r];
                    total += value;
                    float level = Mathf.Clamp01(value / Mathf.Max(1f, contribPeak[r]));
                    Color32 color = ContribColor(level, c == ContribColumns - 1);
                    // Row 0 is the top row on screen; textures start at the bottom.
                    // Quiet cells are 2px specks, busy ones grow into rounded 4px dots.
                    int dot = level < 0.04f ? 2 : 4;
                    int px = c * ContribCell + (ContribCell - dot) / 2;
                    int py = (ContribRows - 1 - r) * ContribCell + (ContribCell - dot) / 2;
                    for (int y = 0; y < dot; y++)
                    {
                        for (int x = 0; x < dot; x++)
                        {
                            bool corner = dot == 4 && (x == 0 || x == 3) && (y == 0 || y == 3);
                            if (!corner)
                                contribPixels[(py + y) * width + px + x] = color;
                        }
                    }
                }
            }
            contribTexture.SetPixels32(contribPixels);
            contribTexture.Apply(false);
            if (contribTotal != null)
                contribTotal.text = TextFormat.Count(Mathf.RoundToInt(total)) + " events";
        }

        static Color32 ContribColor(float level, bool live)
        {
            if (level < 0.04f)
                return new Color32(60, 120, 90, 90);
            int step = level < 0.3f ? 1 : level < 0.6f ? 2 : level < 0.85f ? 3 : 4;
            Color32 color;
            switch (step)
            {
                case 1: color = new Color32(30, 110, 66, 255); break;
                case 2: color = new Color32(42, 170, 96, 255); break;
                case 3: color = new Color32(70, 225, 130, 255); break;
                default: color = new Color32(200, 255, 220, 255); break;
            }
            if (live)
                color = Color32.Lerp(color, new Color32(230, 255, 240, 255), 0.3f);
            return color;
        }
    }
}
