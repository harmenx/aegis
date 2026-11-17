using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Aegis
{
    // Labelled callouts for recently active places, drawn in the dashboard's UI layer.
    public partial class ConnectionGlobe
    {
        const int MaxCallouts = 4;
        const float CalloutLife = 7f;
        // Panel-space box a callout's text roughly occupies; a second callout inside it is skipped.
        const float CalloutClearX = 150f;
        const float CalloutClearY = 40f;

        readonly List<Callout> callouts = new List<Callout>(MaxCallouts);
        readonly List<CalloutView> calloutViews = new List<CalloutView>(MaxCallouts);
        readonly List<Vector2> placedCallouts = new List<Vector2>(MaxCallouts);
        VisualElement calloutLayer;
        VisualElement calloutRoot;

        class Callout
        {
            public string Name;
            public string Detail;
            public Vector3 Direction;
            public float Age;
        }

        class CalloutView
        {
            public VisualElement Root;
            public VisualElement Leader;
            public VisualElement Dot;
            public Label Title;
            public Label Detail;
        }

        void RememberCallout(string name, string detail, Vector3 direction)
        {
            if (string.IsNullOrEmpty(name))
                return;
            string label = name.ToLowerInvariant();
            for (int i = 0; i < callouts.Count; i++)
            {
                if (callouts[i].Name != label)
                    continue;
                callouts[i].Direction = direction;
                callouts[i].Detail = detail;
                if (callouts[i].Age > 5.4f)
                    callouts[i].Age = 5.4f;
                return;
            }
            if (callouts.Count >= MaxCallouts)
            {
                int oldest = 0;
                for (int i = 1; i < callouts.Count; i++)
                {
                    if (callouts[i].Age > callouts[oldest].Age)
                        oldest = i;
                }
                if (callouts[oldest].Age < 2.6f)
                    return;
                callouts.RemoveAt(oldest);
            }
            callouts.Insert(0, new Callout { Name = label, Detail = detail, Direction = direction, Age = 0f });
        }

        void AgeCallouts(float dt)
        {
            for (int i = callouts.Count - 1; i >= 0; i--)
            {
                callouts[i].Age += dt;
                if (callouts[i].Age > CalloutLife)
                    callouts.RemoveAt(i);
            }
        }

        bool EnsureCalloutLayer()
        {
            if (calloutLayer != null && calloutLayer.panel != null)
                return true;
            DashboardController dashboard = FindAnyObjectByType<DashboardController>();
            if (dashboard == null)
                return false;
            UIDocument document = dashboard.GetComponent<UIDocument>();
            if (document == null || document.rootVisualElement == null || document.rootVisualElement.panel == null)
                return false;
            calloutRoot = document.rootVisualElement.Q<VisualElement>("root") ?? document.rootVisualElement;
            calloutLayer = calloutRoot.Q<VisualElement>("city-callouts");
            if (calloutLayer == null)
            {
                calloutLayer = new VisualElement { name = "city-callouts", pickingMode = PickingMode.Ignore };
                calloutLayer.AddToClassList("callout-layer");
                calloutRoot.Insert(0, calloutLayer);
            }
            calloutViews.Clear();
            calloutLayer.Clear();
            return true;
        }

        CalloutView MakeCalloutView()
        {
            var view = new CalloutView { Root = new VisualElement { pickingMode = PickingMode.Ignore } };
            view.Root.AddToClassList("callout");
            view.Leader = new VisualElement { pickingMode = PickingMode.Ignore };
            view.Leader.AddToClassList("callout-leader");
            view.Dot = new VisualElement { pickingMode = PickingMode.Ignore };
            view.Dot.AddToClassList("callout-dot");
            // Place names come from outside data: show them as typed, never as markup.
            view.Title = new Label { pickingMode = PickingMode.Ignore, enableRichText = false };
            view.Title.AddToClassList("callout-title");
            view.Detail = new Label { pickingMode = PickingMode.Ignore, enableRichText = false };
            view.Detail.AddToClassList("callout-detail");
            view.Root.Add(view.Leader);
            view.Root.Add(view.Dot);
            view.Root.Add(view.Title);
            view.Root.Add(view.Detail);
            calloutLayer.Add(view.Root);
            calloutViews.Add(view);
            return view;
        }

        void HideCallouts()
        {
            for (int i = 0; i < calloutViews.Count; i++)
                calloutViews[i].Root.style.display = DisplayStyle.None;
        }

        void DrawCallouts(Camera cam)
        {
            if (!EnsureCalloutLayer())
                return;
            if (Alpha < 0.4f || callouts.Count == 0)
            {
                HideCallouts();
                return;
            }

            Vector3 towardCamera = (cam.transform.position - transform.position).normalized;
            // Viewport space keeps labels pinned however the panel is scaled or rendered.
            Vector3 centreViewport = cam.WorldToViewportPoint(transform.position);
            Rect panelRect = calloutRoot.panel.visualTree.layout;
            Vector2 panelSize = new Vector2(panelRect.width, panelRect.height);
            if (float.IsNaN(panelSize.x) || panelSize.x <= 0f)
                return;
            Vector2 centre = new Vector2(centreViewport.x * panelSize.x, (1f - centreViewport.y) * panelSize.y);

            int shown = 0;
            placedCallouts.Clear();
            // Newest first, so when two places crowd each other the fresher one keeps its label.
            for (int i = 0; i < callouts.Count; i++)
            {
                Callout callout = callouts[i];
                Vector3 anchorWorld = transform.TransformPoint(callout.Direction * Radius);
                Vector3 viewport = cam.WorldToViewportPoint(anchorWorld);
                float facing = Vector3.Dot(transform.TransformDirection(callout.Direction), towardCamera);
                float onFace = Mathf.SmoothStep(0.05f, 0.4f, facing);
                float fade = Mathf.Clamp01(callout.Age / 0.45f) * Mathf.Clamp01((CalloutLife - callout.Age) / 1.4f) * onFace;
                if (viewport.z < 0.1f || fade < 0.04f)
                    continue;

                Vector2 anchor = new Vector2(viewport.x * panelSize.x, (1f - viewport.y) * panelSize.y);
                // Labels lean away from the globe's centre so they sit off the busy disc.
                bool leftSide = anchor.x < centre.x;
                float rise = 34f + (i % 2) * 22f;
                Vector2 textAt = new Vector2(anchor.x + (leftSide ? -CalloutClearX * 0.5f : CalloutClearX * 0.5f), anchor.y - rise);
                if (Crowded(textAt))
                    continue;
                placedCallouts.Add(textAt);

                CalloutView view = shown < calloutViews.Count ? calloutViews[shown] : MakeCalloutView();
                view.Root.style.display = DisplayStyle.Flex;
                view.Root.style.left = anchor.x;
                view.Root.style.top = anchor.y - rise;
                view.Root.style.opacity = fade;
                view.Leader.style.height = rise;
                view.Dot.style.top = rise - 2f;
                view.Title.text = TextFormat.Ellipsize(callout.Name, 24);
                view.Detail.text = TextFormat.Ellipsize(callout.Detail, 30);
                float textOffset = leftSide ? -6f : 6f;
                var shift = new Translate(new Length(leftSide ? -100 : 0, LengthUnit.Percent), 0);
                view.Title.style.translate = shift;
                view.Detail.style.translate = shift;
                view.Title.style.left = textOffset;
                view.Detail.style.left = textOffset;
                shown++;
            }
            for (int i = shown; i < calloutViews.Count; i++)
                calloutViews[i].Root.style.display = DisplayStyle.None;
        }

        bool Crowded(Vector2 textAt)
        {
            for (int i = 0; i < placedCallouts.Count; i++)
            {
                Vector2 other = placedCallouts[i];
                if (Mathf.Abs(other.x - textAt.x) < CalloutClearX && Mathf.Abs(other.y - textAt.y) < CalloutClearY)
                    return true;
            }
            return false;
        }
    }
}
