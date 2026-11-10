using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gitmos
{
    // Thin line icons for panel corners, drawn with the vector API so no icon font is needed.
    public static class Icons
    {
        static readonly Color Ink = new Color(0.55f, 0.6f, 0.7f, 0.85f);
        const float Stroke = 1.2f;

        public static void Attach(VisualElement element, Action<Painter2D, float> draw)
        {
            if (element == null)
                return;
            element.generateVisualContent += context =>
            {
                float size = Mathf.Min(element.contentRect.width, element.contentRect.height);
                if (size <= 0f)
                    return;
                Painter2D painter = context.painter2D;
                painter.strokeColor = Ink;
                painter.fillColor = Ink;
                painter.lineWidth = Stroke;
                painter.lineCap = LineCap.Round;
                painter.lineJoin = LineJoin.Round;
                draw(painter, size);
            };
            element.MarkDirtyRepaint();
        }

        static void Circle(Painter2D p, Vector2 centre, float radius)
        {
            p.BeginPath();
            p.Arc(centre, radius, Angle.Degrees(0f), Angle.Degrees(360f));
            p.ClosePath();
            p.Stroke();
        }

        static void Line(Painter2D p, Vector2 a, Vector2 b)
        {
            p.BeginPath();
            p.MoveTo(a);
            p.LineTo(b);
            p.Stroke();
        }

        public static void Fork(Painter2D p, float s)
        {
            float r = s * 0.12f;
            Circle(p, new Vector2(s * 0.25f, s * 0.18f), r);
            Circle(p, new Vector2(s * 0.75f, s * 0.18f), r);
            Circle(p, new Vector2(s * 0.5f, s * 0.84f), r);
            p.BeginPath();
            p.MoveTo(new Vector2(s * 0.25f, s * 0.3f));
            p.BezierCurveTo(new Vector2(s * 0.25f, s * 0.55f), new Vector2(s * 0.5f, s * 0.45f), new Vector2(s * 0.5f, s * 0.72f));
            p.Stroke();
            p.BeginPath();
            p.MoveTo(new Vector2(s * 0.75f, s * 0.3f));
            p.BezierCurveTo(new Vector2(s * 0.75f, s * 0.55f), new Vector2(s * 0.5f, s * 0.45f), new Vector2(s * 0.5f, s * 0.72f));
            p.Stroke();
        }

        public static void Octo(Painter2D p, float s)
        {
            Vector2 c = new Vector2(s * 0.5f, s * 0.5f);
            Circle(p, c, s * 0.44f);
            // A small cat-like head inside the ring.
            p.BeginPath();
            p.MoveTo(new Vector2(s * 0.32f, s * 0.62f));
            p.LineTo(new Vector2(s * 0.32f, s * 0.32f));
            p.LineTo(new Vector2(s * 0.4f, s * 0.4f));
            p.LineTo(new Vector2(s * 0.6f, s * 0.4f));
            p.LineTo(new Vector2(s * 0.68f, s * 0.32f));
            p.LineTo(new Vector2(s * 0.68f, s * 0.62f));
            p.Stroke();
            Line(p, new Vector2(s * 0.45f, s * 0.72f), new Vector2(s * 0.45f, s * 0.9f));
            Line(p, new Vector2(s * 0.55f, s * 0.72f), new Vector2(s * 0.55f, s * 0.9f));
        }

        public static void List(Painter2D p, float s)
        {
            for (int i = 0; i < 3; i++)
            {
                float y = s * (0.25f + i * 0.25f);
                Line(p, new Vector2(s * 0.12f, y), new Vector2(s * 0.88f, y));
            }
        }

        public static void Chat(Painter2D p, float s)
        {
            p.BeginPath();
            p.MoveTo(new Vector2(s * 0.08f, s * 0.12f));
            p.LineTo(new Vector2(s * 0.68f, s * 0.12f));
            p.LineTo(new Vector2(s * 0.68f, s * 0.56f));
            p.LineTo(new Vector2(s * 0.3f, s * 0.56f));
            p.LineTo(new Vector2(s * 0.14f, s * 0.72f));
            p.LineTo(new Vector2(s * 0.14f, s * 0.56f));
            p.LineTo(new Vector2(s * 0.08f, s * 0.56f));
            p.ClosePath();
            p.Stroke();
            p.BeginPath();
            p.MoveTo(new Vector2(s * 0.78f, s * 0.3f));
            p.LineTo(new Vector2(s * 0.92f, s * 0.3f));
            p.LineTo(new Vector2(s * 0.92f, s * 0.76f));
            p.LineTo(new Vector2(s * 0.86f, s * 0.76f));
            p.LineTo(new Vector2(s * 0.86f, s * 0.9f));
            p.LineTo(new Vector2(s * 0.72f, s * 0.76f));
            p.LineTo(new Vector2(s * 0.4f, s * 0.76f));
            p.LineTo(new Vector2(s * 0.4f, s * 0.66f));
            p.Stroke();
        }

        public static void Gear(Painter2D p, float s)
        {
            Vector2 c = new Vector2(s * 0.5f, s * 0.5f);
            Circle(p, c, s * 0.42f);
            Circle(p, c, s * 0.14f);
        }

        public static void Cycle(Painter2D p, float s)
        {
            Vector2 c = new Vector2(s * 0.5f, s * 0.5f);
            Circle(p, c, s * 0.42f);
            Line(p, new Vector2(s * 0.5f, s * 0.28f), new Vector2(s * 0.5f, s * 0.72f));
            Line(p, new Vector2(s * 0.28f, s * 0.5f), new Vector2(s * 0.72f, s * 0.5f));
        }
    }
}
