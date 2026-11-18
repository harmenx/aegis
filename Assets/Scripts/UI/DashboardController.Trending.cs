using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Aegis
{
    // Trending Developers panel: actors ranked by point count, with their latest repo.
    public partial class DashboardController
    {
        const int TrendingRows = 10;
        // Caps memory on long runs with many distinct labels; the quietest actors are dropped.
        const int MaxTrackedActors = 2000;

        Label hotLanguage;
        VisualElement trending;
        readonly Dictionary<string, ActorStat> actors = new Dictionary<string, ActorStat>();
        readonly List<ActorStat> ranking = new List<ActorStat>();
        readonly List<VisualElement> fading = new List<VisualElement>();
        readonly HashSet<string> knownTrend = new HashSet<string>();
        readonly Dictionary<string, Texture2D> faces = new Dictionary<string, Texture2D>();
        readonly StringBuilder trendingKeyText = new StringBuilder();
        string trendingKey = "";
        string dominantLanguage = "TypeScript";

        class ActorStat
        {
            public string Name;
            public string Repo;
            public string Language;
            public int Count;
        }

        void SeedTrending()
        {
            for (int i = 0; i < 8 && i < Catalog.Actors.Length && i < Catalog.Repos.Length; i++)
            {
                RepoInfo repo = Catalog.Repos[i];
                actors[Catalog.Actors[i]] = new ActorStat
                {
                    Name = Catalog.Actors[i],
                    Repo = repo.Name,
                    Language = repo.Language,
                    Count = 18 - i
                };
            }
        }

        void CountActor(GitEvent gitEvent)
        {
            string actor = string.IsNullOrEmpty(gitEvent.Actor) ? "anonymous" : gitEvent.Actor;
            if (!actors.TryGetValue(actor, out ActorStat stat))
            {
                if (actors.Count >= MaxTrackedActors)
                    PruneActors();
                stat = new ActorStat { Name = actor };
                actors.Add(actor, stat);
            }
            stat.Count++;
            if (!string.IsNullOrEmpty(gitEvent.Repo))
                stat.Repo = gitEvent.Repo;
            if (!string.IsNullOrEmpty(gitEvent.Language))
                stat.Language = gitEvent.Language;
        }

        void PruneActors()
        {
            ranking.Clear();
            ranking.AddRange(actors.Values);
            ranking.Sort((a, b) => a.Count.CompareTo(b.Count));
            int drop = actors.Count / 4;
            for (int i = 0; i < drop; i++)
            {
                string name = ranking[i].Name;
                // Never drop someone currently on screen; their avatar texture is in use.
                if (trendingKey.Contains(name + "\u0001"))
                    continue;
                actors.Remove(name);
                knownTrend.Remove(name);
                if (faces.TryGetValue(name, out Texture2D face))
                {
                    DashboardTextures.Destroy(face);
                    faces.Remove(name);
                }
            }
            ranking.Clear();
        }

        void RebuildTrending()
        {
            if (trending == null)
                return;

            ranking.Clear();
            ranking.AddRange(actors.Values);
            ranking.Sort((a, b) => b.Count.CompareTo(a.Count));

            if (ranking.Count > 0 && !string.IsNullOrEmpty(ranking[0].Language))
                dominantLanguage = ranking[0].Language;

            int listed = Mathf.Min(TrendingRows, ranking.Count);
            trendingKeyText.Clear();
            for (int i = 0; i < listed; i++)
                trendingKeyText.Append(ranking[i].Name).Append('\u0001');
            string nextKey = trendingKeyText.ToString();
            if (nextKey == trendingKey)
            {
                // Same order: just refresh the stats in place.
                for (int i = 0; i < listed && i < trending.childCount; i++)
                {
                    Label stats = trending[i].Q<Label>(className: "trend-stats");
                    if (stats != null)
                        stats.text = Stats(ranking[i]);
                }
                return;
            }
            trendingKey = nextKey;

            trending.Clear();
            for (int i = 0; i < listed; i++)
            {
                ActorStat stat = ranking[i];
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("trend-row");

                row.Add(PlainLabel((i + 1).ToString(), "trend-rank"));
                row.Add(Avatar(stat.Name, Palette.ForLanguage(stat.Language), "avatar", 30f));

                var text = new VisualElement { pickingMode = PickingMode.Ignore };
                text.AddToClassList("trend-text");
                text.Add(PlainLabel(TextFormat.Ellipsize(stat.Name, 18), "trend-name"));
                text.Add(PlainLabel(Stats(stat), "trend-stats"));
                row.Add(text);

                row.Add(PlainLabel(TextFormat.Ellipsize(TextFormat.ShortRepo(stat.Repo), 18), "trend-repo"));

                // Lower ranks recede.
                float rankFade = Mathf.Lerp(1f, 0.35f, Mathf.InverseLerp(6f, 9f, i));
                row.userData = rankFade;
                if (knownTrend.Add(stat.Name))
                {
                    row.style.opacity = 0f;
                    fading.Add(row);
                }
                else
                {
                    row.style.opacity = rankFade;
                }
                trending.Add(row);
            }
        }

        // Labels that show outside text never interpret it as markup.
        static Label PlainLabel(string text, string className)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore, enableRichText = false };
            label.AddToClassList(className);
            return label;
        }

        static string Stats(ActorStat stat)
        {
            string language = string.IsNullOrEmpty(stat.Language) ? "" : "   " + TextFormat.Ellipsize(stat.Language, 14);
            return "↑ " + TextFormat.Count(stat.Count) + language;
        }

        void TickFades(float dt)
        {
            for (int i = fading.Count - 1; i >= 0; i--)
            {
                VisualElement element = fading[i];
                if (element == null || element.panel == null)
                {
                    fading.RemoveAt(i);
                    continue;
                }
                float target = element.userData is float limit ? limit : 1f;
                float next = Mathf.MoveTowards(element.style.opacity.value, target, dt / 0.35f);
                element.style.opacity = next;
                if (next >= target - 0.01f)
                    fading.RemoveAt(i);
            }
        }

        VisualElement Avatar(string name, Color color, string avatarClass, float size)
        {
            if (string.IsNullOrEmpty(name))
                name = "?";
            if (!faces.TryGetValue(name, out Texture2D face))
            {
                face = DashboardTextures.Identicon(name, color);
                faces[name] = face;
            }
            var avatar = new VisualElement { pickingMode = PickingMode.Ignore };
            avatar.AddToClassList(avatarClass);
            avatar.style.backgroundImage = new StyleBackground(face);
            Round(avatar, size);
            return avatar;
        }

        static void Round(VisualElement element, float size)
        {
            float radius = size * 0.5f;
            element.style.width = size;
            element.style.height = size;
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }
    }
}
