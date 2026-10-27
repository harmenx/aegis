using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gitmos
{
    public class DashboardController : MonoBehaviour
    {
        public Font monoFont;
        public PanelSettings panelSettings;

        public int ForkedProjects => forks;
        public int StreamLines => filledStreamLines;
        public string StageTitle => stageTitle != null ? stageTitle.text : "";
        public event Action<int> StageChosen;

        UIDocument document;
        VisualElement root;
        Label forksLabel;
        Label stageTitle;
        Label stageCount;
        Label clock;
        Label streamClock;
        Label linesAdded;
        Label linesDeleted;
        Label reviewCount;
        Label hotLanguage;
        VisualElement trending;
        VisualElement stream;
        VisualElement reviewers;
        VisualElement contrib;
        readonly Button[] toggles = new Button[4];

        readonly Dictionary<string, ActorStat> actors = new Dictionary<string, ActorStat>();
        readonly List<ActorStat> ranking = new List<ActorStat>();
        readonly List<Reviewer> recentReviews = new List<Reviewer>();
        Label[] streamLabels;
        float[] columns;
        Texture2D contribTexture;
        Texture2D vignetteTexture;

        int forks = 1278;
        int collaborations = 375;
        int activity = 860;
        int linesPlus = 3094109;
        int linesMinus = 186420;
        int reviews = 3049;
        int resolved = 96;
        int stageIndex;
        int filledStreamLines;
        float refresh;
        float columnShift;
        string dominantLanguage = "TypeScript";

        class ActorStat
        {
            public string Name;
            public string Repo;
            public string Language;
            public int Count;
        }

        struct Reviewer
        {
            public string Name;
            public string Repo;
            public Color Color;
        }

        void Awake()
        {
            document = GetComponent<UIDocument>();
            if (document == null)
                return;
            if (document.panelSettings == null && panelSettings != null)
                document.panelSettings = panelSettings;
            document.sortingOrder = 10;
        }

        void OnEnable()
        {
            if (!Application.isPlaying)
                return;
            document = GetComponent<UIDocument>();
            EventBus.OnEvent += HandleEvent;
        }

        void Start()
        {
            if (!Application.isPlaying)
                return;
            Bind();
        }

        void OnDisable()
        {
            EventBus.OnEvent -= HandleEvent;
        }

        void Bind()
        {
            if (document == null || document.rootVisualElement == null)
            {
                Debug.LogWarning("Gitmos dashboard has no visual tree.");
                return;
            }
            Debug.Log("Gitmos dashboard overlays bound.");

            root = document.rootVisualElement.Q<VisualElement>("root");
            if (root == null)
                root = document.rootVisualElement;

            root.pickingMode = PickingMode.Ignore;
            if (monoFont != null)
                root.style.unityFontDefinition = FontDefinition.FromFont(monoFont);
            forksLabel = root.Q<Label>("forks");
            stageTitle = root.Q<Label>("stage-title");
            stageCount = root.Q<Label>("stage-count");
            clock = root.Q<Label>("clock");
            streamClock = root.Q<Label>("stream-clock");
            linesAdded = root.Q<Label>("lines-added");
            linesDeleted = root.Q<Label>("lines-deleted");
            reviewCount = root.Q<Label>("review-count");
            hotLanguage = root.Q<Label>("hot-language");
            trending = root.Q<VisualElement>("trending");
            stream = root.Q<VisualElement>("stream");
            reviewers = root.Q<VisualElement>("reviewers");
            contrib = root.Q<VisualElement>("contrib");
            BindToggles();

            var hole = root.Q<VisualElement>("stage-hole");
            if (hole != null)
                hole.pickingMode = PickingMode.Ignore;

            columns = new float[40];
            for (int i = 0; i < columns.Length; i++)
            {
                float t = i / (float)(columns.Length - 1);
                columns[i] = 0.22f + 0.62f * Mathf.PerlinNoise(t * 3.4f, 0.35f);
            }
            SeedChrome();

            if (stream != null)
            {
                streamLabels = new Label[32];
                for (int i = 0; i < streamLabels.Length; i++)
                {
                    var label = new Label();
                    label.AddToClassList("stream-line");
                    label.enableRichText = true;
                    label.style.display = DisplayStyle.None;
                    ApplyMono(label);
                    stream.Add(label);
                    streamLabels[i] = label;
                }
            }

            contribTexture = new Texture2D(320, 36, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            if (contrib != null)
                contrib.style.backgroundImage = new StyleBackground(contribTexture);

            var vignette = root.Q<VisualElement>("vignette");
            if (vignette != null)
            {
                vignette.pickingMode = PickingMode.Ignore;
                vignetteTexture = BuildVignette(256);
                vignette.style.backgroundImage = new StyleBackground(vignetteTexture);
            }

            PaintContrib(true);
            RefreshNumbers();
        }

        void HandleEvent(GitEvent gitEvent)
        {
            activity++;
            linesPlus += Mathf.Max(0, gitEvent.Additions);
            linesMinus += Mathf.Max(0, gitEvent.Deletions);

            if (gitEvent.Type == GitEventType.Fork)
                forks++;
            if (gitEvent.Type == GitEventType.PullRequest || gitEvent.Type == GitEventType.Review || gitEvent.Type == GitEventType.Merge)
                collaborations++;
            if (gitEvent.Type == GitEventType.Review || gitEvent.Type == GitEventType.Merge)
            {
                reviews++;
                resolved++;
                recentReviews.Insert(0, new Reviewer
                {
                    Name = gitEvent.Actor,
                    Repo = ShortRepo(gitEvent.Repo),
                    Color = Palette.ForLanguage(gitEvent.Language)
                });
            if (recentReviews.Count > 16)
                recentReviews.RemoveAt(recentReviews.Count - 1);
            }

            if (!actors.TryGetValue(gitEvent.Actor, out ActorStat stat))
            {
                stat = new ActorStat { Name = gitEvent.Actor };
                actors.Add(gitEvent.Actor, stat);
            }
            stat.Count++;
            stat.Repo = ShortRepo(gitEvent.Repo);
            stat.Language = gitEvent.Language;

            if (columns != null && columns.Length > 0)
                columns[columns.Length - 1] = Mathf.Clamp01(columns[columns.Length - 1] + 0.14f);

            PushStream(gitEvent);
        }

        void Update()
        {
            refresh += Time.deltaTime;
            columnShift += Time.deltaTime;
            if (columns != null && columnShift > 0.85f)
            {
                columnShift = 0f;
                for (int i = 0; i < columns.Length - 1; i++)
                    columns[i] = columns[i + 1] * 0.99f;
                columns[columns.Length - 1] = 0.18f;
                PaintContrib(false);
            }

            if (clock != null && refresh > 0.25f)
            {
                string now = System.DateTime.Now.ToString("HH:mm:ss");
                clock.text = now;
                if (streamClock != null)
                    streamClock.text = now;
            }

            if (refresh > 0.3f)
            {
                refresh = 0f;
                RefreshNumbers();
                RebuildTrending();
                RebuildReviewers();
            }
        }

        public void SetStage(int index, string title)
        {
            stageIndex = index;
            if (stageTitle != null && stageTitle.text != title)
                stageTitle.text = title;
            for (int i = 0; i < toggles.Length; i++)
            {
                if (toggles[i] == null)
                    continue;
                if (i == index)
                    toggles[i].AddToClassList("toggle-on");
                else
                    toggles[i].RemoveFromClassList("toggle-on");
            }
            RefreshNumbers();
        }

        void BindToggles()
        {
            for (int i = 0; i < toggles.Length; i++)
            {
                int index = i;
                Button button = root.Q<Button>("toggle-" + i);
                toggles[i] = button;
                if (button == null)
                    continue;
                button.focusable = false;
                button.clicked += () =>
                {
                    if (StageChosen != null)
                        StageChosen(index);
                };
            }
        }

        public void SetChromeOpacity(float opacity)
        {
            if (root != null)
                root.style.opacity = Mathf.Clamp01(opacity);
        }

        public bool PointerOverUi(Vector2 screenPosition)
        {
            if (document == null || document.rootVisualElement == null)
                return false;
            var panel = document.rootVisualElement.panel;
            if (panel == null)
                return false;
            Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(panel, screenPosition);
            VisualElement picked = panel.Pick(panelPosition);
            return picked != null && picked.pickingMode == PickingMode.Position;
        }

        void RefreshNumbers()
        {
            if (forksLabel != null)
                forksLabel.text = forks.ToString("N0");
            if (linesAdded != null)
                linesAdded.text = Compact(linesPlus);
            if (linesDeleted != null)
                linesDeleted.text = "−" + Compact(linesMinus);
            if (reviewCount != null)
                reviewCount.text = reviews.ToString("N0");
            if (hotLanguage != null)
                hotLanguage.text = dominantLanguage;
            if (stageCount == null)
                return;

            int shown = collaborations;
            if (stageIndex == 1 || stageIndex == 2)
                shown = activity;
            else if (stageIndex == 3)
                shown = resolved;
            stageCount.text = shown.ToString("N0");
        }

        void RebuildTrending()
        {
            if (trending == null)
                return;

            ranking.Clear();
            foreach (var pair in actors)
                ranking.Add(pair.Value);
            ranking.Sort((a, b) => b.Count.CompareTo(a.Count));

            if (ranking.Count > 0 && !string.IsNullOrEmpty(ranking[0].Language))
                dominantLanguage = ranking[0].Language;

            trending.Clear();
            int count = Mathf.Min(8, ranking.Count);
            for (int i = 0; i < count; i++)
            {
                ActorStat stat = ranking[i];
                var row = new VisualElement();
                row.AddToClassList("trend-row");
                row.pickingMode = PickingMode.Ignore;
                row.Add(Avatar(stat.Name, Palette.ForLanguage(stat.Language), "avatar"));

                var name = new Label(Trim(stat.Name, 16));
                name.AddToClassList("trend-name");
                ApplyMono(name);
                row.Add(name);
                trending.Add(row);
            }
        }

        VisualElement Avatar(string name, Color color, string avatarClass)
        {
            var avatar = new VisualElement();
            avatar.AddToClassList(avatarClass);
            avatar.style.backgroundColor = color;
            avatar.pickingMode = PickingMode.Ignore;
            string initial = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();
            var letter = new Label(initial);
            letter.AddToClassList("avatar-letter");
            letter.pickingMode = PickingMode.Ignore;
            ApplyMono(letter);
            avatar.Add(letter);
            return avatar;
        }

        void RebuildReviewers()
        {
            if (reviewers == null)
                return;
            reviewers.Clear();
            int count = Mathf.Min(12, recentReviews.Count);
            for (int i = 0; i < count; i++)
            {
                Reviewer reviewer = recentReviews[i];
                var column = new VisualElement();
                column.AddToClassList("reviewer");
                column.pickingMode = PickingMode.Ignore;
                column.Add(Avatar(reviewer.Name, reviewer.Color, "reviewer-avatar"));

                var name = new Label(Trim(reviewer.Name, 7));
                name.AddToClassList("reviewer-name");
                ApplyMono(name);
                column.Add(name);
                reviewers.Add(column);
            }
        }

        void PushStream(GitEvent gitEvent)
        {
            if (streamLabels == null)
                return;
            for (int i = streamLabels.Length - 1; i > 0; i--)
            {
                streamLabels[i].text = streamLabels[i - 1].text;
                streamLabels[i].style.display = streamLabels[i - 1].style.display;
            }
            streamLabels[0].text = Format(gitEvent);
            streamLabels[0].style.display = DisplayStyle.Flex;
            filledStreamLines = Mathf.Min(streamLabels.Length, filledStreamLines + 1);
        }

        static string Format(GitEvent gitEvent)
        {
            string kind = string.IsNullOrEmpty(gitEvent.Category) ? TypeLabel(gitEvent.Type) : gitEvent.Category;
            string subject = string.IsNullOrEmpty(gitEvent.Repo) ? gitEvent.Actor : gitEvent.Repo;
            string hex = gitEvent.Type == GitEventType.Delete ? "ff5d6c" : "39e58a";
            if (gitEvent.Type != GitEventType.Delete && gitEvent.Type != GitEventType.Push
                && gitEvent.Type != GitEventType.Create && gitEvent.Type != GitEventType.Merge)
                hex = "d5dde6";
            if (gitEvent.Tint.a > 0.01f)
                hex = ColorUtility.ToHtmlStringRGB(gitEvent.Tint);
            return "<color=#" + hex + ">" + kind + "  " + subject + "</color>";
        }

        static string Compact(int value)
        {
            if (value >= 1000000000)
                return (value / 1000000).ToString("N0") + "m";
            if (value >= 1000000)
                return (value / 1000).ToString("N0") + "k";
            return value.ToString("N0");
        }

        static string TypeLabel(GitEventType type)
        {
            switch (type)
            {
                case GitEventType.Push: return "PushEvent";
                case GitEventType.PullRequest: return "PullRequest";
                case GitEventType.Fork: return "ForkEvent";
                case GitEventType.Create: return "CreateEvent";
                case GitEventType.Merge: return "PullRequestMerge";
                case GitEventType.Delete: return "DeleteEvent";
                case GitEventType.Review: return "PullRequestReview";
                default: return type.ToString();
            }
        }

        void PaintContrib(bool force)
        {
            if (contribTexture == null || columns == null)
                return;
            const int width = 320;
            const int height = 36;
            const int cols = 40;
            const int rows = 7;
            var pixels = new Color32[width * height];
            for (int col = 0; col < cols; col++)
            {
                float level = columns[Mathf.Min(col, columns.Length - 1)];
                for (int row = 0; row < rows; row++)
                {
                    float noise = CellNoise(col, row);
                    if (noise > 0.2f + level * 0.32f)
                        continue;
                    float bright = Mathf.Clamp01(level * (0.45f + noise));
                    byte g = (byte)Mathf.Lerp(90f, 255f, bright);
                    byte r = (byte)Mathf.Lerp(8f, 40f, bright);
                    byte b = (byte)Mathf.Lerp(36f, 96f, bright);
                    int ox = col * 8;
                    int oy = row * 5;
                    for (int y = 0; y < 3; y++)
                    {
                        int py = oy + y;
                        if (py >= height)
                            continue;
                        for (int x = 0; x < 3; x++)
                        {
                            int px = ox + x;
                            if (px >= width)
                                continue;
                            pixels[py * width + px] = new Color32(r, g, b, 255);
                        }
                    }
                }
            }
            contribTexture.SetPixels32(pixels);
            contribTexture.Apply(false);
            if (force && contrib != null)
                contrib.style.backgroundImage = new StyleBackground(contribTexture);
        }

        void SeedChrome()
        {
            for (int i = 0; i < 8 && i < Catalog.Actors.Length && i < Catalog.Repos.Length; i++)
            {
                RepoInfo repo = Catalog.Repos[i];
                actors[Catalog.Actors[i]] = new ActorStat
                {
                    Name = Catalog.Actors[i],
                    Repo = ShortRepo(repo.Name),
                    Language = repo.Language,
                    Count = 18 - i
                };
            }

            string[] names = { "orbit-lane", "northwind", "keel", "paperkite", "glassfork", "halcyon", "lattice", "harbor", "inkwell", "cedar" };
            string[] languages = { "TypeScript", "Python", "Rust", "Go", "JavaScript", "C#", "Swift", "Ruby", "Kotlin", "Java" };
            for (int i = 0; i < names.Length; i++)
            {
                recentReviews.Add(new Reviewer
                {
                    Name = names[i],
                    Repo = "",
                    Color = Palette.ForLanguage(languages[i])
                });
            }
        }

        static float CellNoise(int x, int y)
        {
            int n = x * 374761393 + y * 668265263;
            n = (n ^ (n >> 13)) * 1274126177;
            return (n & 0xFFFF) / 65535f;
        }

        void ApplyMono(Label label)
        {
            if (monoFont != null)
                label.style.unityFontDefinition = FontDefinition.FromFont(monoFont);
        }

        static Texture2D BuildVignette(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color32[size * size];
            float center = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - center) / center;
                    float dy = (y - center) / center;
                    float d = Mathf.Sqrt(dx * dx * 0.85f + dy * dy);
                    float alpha = Mathf.SmoothStep(0.78f, 1.4f, d) * 0.28f;
                    pixels[y * size + x] = new Color32(0, 0, 0, (byte)(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false);
            return texture;
        }

        static string ShortRepo(string repo)
        {
            if (string.IsNullOrEmpty(repo))
                return "";
            int slash = repo.LastIndexOf('/');
            return slash >= 0 ? repo.Substring(slash + 1) : repo;
        }

        static string Trim(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max)
                return value;
            return value.Substring(0, max - 1) + "…";
        }
    }
}
