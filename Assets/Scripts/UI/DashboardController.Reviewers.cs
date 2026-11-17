using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Aegis
{
    // Users Reviewing & Commenting panel: one bubble per review drifting across a 40 s timeline.
    public partial class DashboardController
    {
        const float ReviewWindow = 40f;
        const int MaxBubbles = 32;
        const int RepoAxisLabels = 4;

        Label reviewCount;
        VisualElement reviewers;
        VisualElement reviewRepos;
        readonly List<Bubble> bubbles = new List<Bubble>(MaxBubbles);
        readonly List<string> recentRepos = new List<string>(8);
        float lastBubbleTime = -100f;

        class Bubble
        {
            public VisualElement Element;
            public float Age;
            public float Size;
        }

        void OnReview(GitEvent gitEvent, int weight)
        {
            // Spread bubbles over the window: a busy feed would otherwise pile them on the right.
            if (Time.time - lastBubbleTime >= ReviewWindow / MaxBubbles)
            {
                lastBubbleTime = Time.time;
                AddBubble(gitEvent.Actor, Palette.ForLanguage(gitEvent.Language), weight, 0f);
            }
            RememberRepo(TextFormat.ShortRepo(gitEvent.Repo));
        }

        void SeedBubbles()
        {
            string[] names = { "orbit-lane", "northwind", "keel", "paperkite", "glassfork", "halcyon", "lattice", "harbor", "inkwell", "cedar", "lowtide", "kindling", "overpass", "byteferry" };
            string[] languages = { "TypeScript", "Python", "Rust", "Go", "JavaScript", "C#", "Swift", "Ruby", "Kotlin", "Java", "Go", "Rust", "Python", "C++" };
            for (int i = 0; i < names.Length; i++)
                AddBubble(names[i], Palette.ForLanguage(languages[i]), Random.Range(10, 600), Random.Range(0f, ReviewWindow * 0.95f));
            for (int i = 0; i < RepoAxisLabels && i < Catalog.Repos.Length; i++)
                RememberRepo(TextFormat.ShortRepo(Catalog.Repos[i * 3 % Catalog.Repos.Length].Name));
        }

        void AddBubble(string name, Color color, int weight, float age)
        {
            if (reviewers == null)
                return;
            Bubble bubble;
            if (bubbles.Count >= MaxBubbles)
            {
                // Reuse the oldest bubble.
                int oldest = 0;
                for (int i = 1; i < bubbles.Count; i++)
                {
                    if (bubbles[i].Age > bubbles[oldest].Age)
                        oldest = i;
                }
                bubble = bubbles[oldest];
            }
            else
            {
                bubble = new Bubble { Element = new VisualElement { pickingMode = PickingMode.Ignore } };
                bubble.Element.AddToClassList("bubble");
                reviewers.Add(bubble.Element);
                bubbles.Add(bubble);
            }
            bubble.Age = age;
            bubble.Size = Mathf.Lerp(6f, 30f, Mathf.Clamp01(weight / 600f));
            // Solid saturated dots; the hue is the language, nudged per person.
            Color.RGBToHSV(color, out float hue, out float saturation, out float _);
            hue = Mathf.Repeat(hue + ((Catalog.StableHash(name) % 9) - 4) * 0.012f, 1f);
            Color dot = Color.HSVToRGB(hue, Mathf.Max(0.6f, saturation), 1f);
            bubble.Element.style.backgroundImage = StyleKeyword.None;
            bubble.Element.style.backgroundColor = dot;
            Round(bubble.Element, bubble.Size);
            bubble.Element.BringToFront();
        }

        void TickBubbles(float dt)
        {
            if (reviewers == null)
                return;
            float width = reviewers.resolvedStyle.width;
            if (float.IsNaN(width) || width <= 0f)
                return;
            const float axis = 22f;
            for (int i = 0; i < bubbles.Count; i++)
            {
                Bubble bubble = bubbles[i];
                bubble.Age += dt;
                float u = bubble.Age / ReviewWindow;
                VisualElement element = bubble.Element;
                if (u >= 1f)
                {
                    element.style.display = DisplayStyle.None;
                    continue;
                }
                element.style.display = DisplayStyle.Flex;
                float grow = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(bubble.Age / 0.4f));
                float size = bubble.Size * grow;
                element.style.left = (1f - u) * (width - bubble.Size) + (bubble.Size - size) * 0.5f;
                element.style.top = axis - size * 0.5f;
                element.style.width = size;
                element.style.height = size;
                element.style.opacity = Mathf.Clamp01((1f - u) / 0.15f);
            }
        }

        void RememberRepo(string repo)
        {
            if (string.IsNullOrEmpty(repo))
                return;
            recentRepos.Remove(repo);
            recentRepos.Insert(0, repo);
            if (recentRepos.Count > RepoAxisLabels)
                recentRepos.RemoveAt(recentRepos.Count - 1);
        }

        void RebuildRepoAxis()
        {
            if (reviewRepos == null)
                return;
            while (reviewRepos.childCount < RepoAxisLabels)
                reviewRepos.Add(PlainLabel("", "review-repo"));
            // Oldest on the left, newest on the right, matching the timeline direction.
            for (int i = 0; i < RepoAxisLabels; i++)
            {
                int source = RepoAxisLabels - 1 - i;
                ((Label)reviewRepos[i]).text = source < recentRepos.Count ? TextFormat.Ellipsize(recentRepos[source], 14) : "";
            }
        }
    }
}
