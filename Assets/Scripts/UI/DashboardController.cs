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

        const int StreamEntries = 24;
        const float StreamEntryHeight = 54f;
        const int ContribColumns = 44;
        const int ContribRows = 7;
        const int ContribCell = 8;
        const float ContribSliceSeconds = 5f;
        const float ReviewWindow = 40f;
        const int MaxBubbles = 32;

        static readonly string[] TabNames = { "Min", "Hour", "Day", "Week" };

        UIDocument document;
        VisualElement root;
        Label forksLabel;
        Label stageTitle;
        Label stageCount;
        Label clock;
        Label streamClock;
        Label streamRate;
        Label linesAdded;
        Label linesDeleted;
        Label reviewCount;
        Label hotLanguage;
        Label contribTotal;
        Label cycleLabel;
        VisualElement trending;
        VisualElement stream;
        VisualElement reviewers;
        VisualElement reviewRepos;
        VisualElement contrib;
        readonly Button[] toggles = new Button[4];
        readonly Button[] tabs = new Button[4];

        readonly Dictionary<string, ActorStat> actors = new Dictionary<string, ActorStat>();
        readonly List<ActorStat> ranking = new List<ActorStat>();
        readonly List<Label> streamLabels = new List<Label>(StreamEntries);
        readonly Queue<string> streamQueue = new Queue<string>();
        readonly List<VisualElement> fading = new List<VisualElement>();
        readonly HashSet<string> knownTrend = new HashSet<string>();
        readonly List<Bubble> bubbles = new List<Bubble>(MaxBubbles);
        readonly List<string> recentRepos = new List<string>(8);
        readonly Dictionary<string, Texture2D> faces = new Dictionary<string, Texture2D>();

        readonly float[] contribCells = new float[ContribColumns * ContribRows];
        readonly float[] contribPeak = new float[ContribRows];
        readonly double[] addedPerSecond = new double[60];
        readonly double[] deletedPerSecond = new double[60];
        Texture2D contribTexture;
        Texture2D vignetteTexture;
        Texture2D gridTexture;
        Color32[] contribPixels;

        int forks = 418;
        int collaborations = 1358;
        int activity = 18640;
        int reviews = 3286;
        int resolved = 96;
        int stageIndex;
        int filledStreamLines;
        int activeTab = 3;
        int secondSlot;
        double hourAdded = 6182400;
        double hourDeleted = 1204800;
        double dayAdded = 148260000;
        double dayDeleted = 26940000;
        double weekAdded = 533938000;
        double weekDeleted = 96420000;
        double shownAdded = 533100000;
        double shownDeleted = 96000000;
        float shownForks = 404f;
        float shownStage = 1312f;
        float shownReviews = 3210f;
        float refresh;
        float clockTick;
        float secondTimer;
        float tabTimer;
        float tabHeld;
        float streamSlide;
        float streamFresh = 1f;
        float streamCooldown;
        float contribTimer;
        float rateWindow;
        int rateCount;
        float rate;
        float stageReveal = 1f;
        bool stageRevealOut;
        string stageRevealTitle = "";
        string trendingKey = "";
        string dominantLanguage = "TypeScript";
        float lastBubbleTime = -100f;

        class ActorStat
        {
            public string Name;
            public string Repo;
            public string Language;
            public int Count;
        }

        class Bubble
        {
            public VisualElement Element;
            public float Age;
            public float Size;
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

        void OnDestroy()
        {
            DestroyTexture(contribTexture);
            DestroyTexture(vignetteTexture);
            DestroyTexture(gridTexture);
            foreach (Texture2D face in faces.Values)
                DestroyTexture(face);
        }

        static void DestroyTexture(Texture2D texture)
        {
            if (texture != null)
                Destroy(texture);
        }

        void Bind()
        {
            if (document == null || document.rootVisualElement == null)
            {
                Debug.LogWarning("Gitmos dashboard has no visual tree.");
                return;
            }

            root = document.rootVisualElement.Q<VisualElement>("root") ?? document.rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            // Layout containers should never swallow orbit drags; only buttons pick.
            root.Query<VisualElement>().ForEach(element =>
            {
                if (!(element is Button))
                    element.pickingMode = PickingMode.Ignore;
            });

            forksLabel = root.Q<Label>("forks");
            stageTitle = root.Q<Label>("stage-title");
            stageCount = root.Q<Label>("stage-count");
            clock = root.Q<Label>("clock");
            streamClock = root.Q<Label>("stream-clock");
            streamRate = root.Q<Label>("stream-rate");
            linesAdded = root.Q<Label>("lines-added");
            linesDeleted = root.Q<Label>("lines-deleted");
            reviewCount = root.Q<Label>("review-count");
            hotLanguage = root.Q<Label>("hot-language");
            contribTotal = root.Q<Label>("contrib-total");
            trending = root.Q<VisualElement>("trending");
            stream = root.Q<VisualElement>("stream");
            reviewers = root.Q<VisualElement>("reviewers");
            reviewRepos = root.Q<VisualElement>("review-repos");
            contrib = root.Q<VisualElement>("contrib");
            BindToggles();
            BindTabs();
            cycleLabel = root.Q<Label>("cycle");
            Icons.Attach(root.Q<VisualElement>("icon-fork"), Icons.Fork);
            Icons.Attach(root.Q<VisualElement>("icon-github"), Icons.Octo);
            Icons.Attach(root.Q<VisualElement>("icon-list"), Icons.List);
            Icons.Attach(root.Q<VisualElement>("icon-chat"), Icons.Chat);
            Icons.Attach(root.Q<VisualElement>("icon-settings"), Icons.Gear);
            Icons.Attach(root.Q<VisualElement>("icon-cycle"), Icons.Cycle);

            var grid = root.Q<VisualElement>("grid");
            if (grid != null)
            {
                gridTexture = BuildGrid(48);
                grid.style.backgroundImage = new StyleBackground(gridTexture);
                grid.style.backgroundRepeat = new StyleBackgroundRepeat(new BackgroundRepeat(Repeat.Repeat, Repeat.Repeat));
                grid.style.backgroundSize = new StyleBackgroundSize(new BackgroundSize(new Length(48f), new Length(48f)));
            }

            var vignette = root.Q<VisualElement>("vignette");
            if (vignette != null)
            {
                vignetteTexture = BuildVignette(256);
                vignette.style.backgroundImage = new StyleBackground(vignetteTexture);
            }

            SeedChrome();
            BuildStream();
            BuildContrib();
            SeedBubbles();
            SelectTab(activeTab, false);
            RebuildTrending();
            RebuildRepoAxis();
        }

        void HandleEvent(GitEvent gitEvent)
        {
            activity++;
            rateCount++;
            int added = Mathf.Max(0, gitEvent.Additions);
            int deleted = Mathf.Max(0, gitEvent.Deletions);
            addedPerSecond[secondSlot] += added;
            deletedPerSecond[secondSlot] += deleted;
            hourAdded += added;
            hourDeleted += deleted;
            dayAdded += added;
            dayDeleted += deleted;
            weekAdded += added;
            weekDeleted += deleted;

            if (gitEvent.Type == GitEventType.Fork)
                forks++;
            if (gitEvent.Type == GitEventType.PullRequest || gitEvent.Type == GitEventType.Review || gitEvent.Type == GitEventType.Merge)
                collaborations++;
            if (gitEvent.Type == GitEventType.Review || gitEvent.Type == GitEventType.Merge)
            {
                reviews++;
                resolved++;
                // Spread bubbles over the window: a busy feed would otherwise pile them on the right.
                if (Time.time - lastBubbleTime >= ReviewWindow / MaxBubbles)
                {
                    lastBubbleTime = Time.time;
                    AddBubble(gitEvent.Actor, Palette.ForLanguage(gitEvent.Language), added, 0f);
                }
                RememberRepo(ShortRepo(gitEvent.Repo));
            }

            string actor = string.IsNullOrEmpty(gitEvent.Actor) ? "anonymous" : gitEvent.Actor;
            if (!actors.TryGetValue(actor, out ActorStat stat))
            {
                stat = new ActorStat { Name = actor };
                actors.Add(actor, stat);
            }
            stat.Count++;
            if (!string.IsNullOrEmpty(gitEvent.Repo))
                stat.Repo = gitEvent.Repo;
            if (!string.IsNullOrEmpty(gitEvent.Language))
                stat.Language = gitEvent.Language;

            int row = string.IsNullOrEmpty(gitEvent.Category)
                ? Mathf.Clamp((int)gitEvent.Type, 0, ContribRows - 1)
                : Catalog.StableHash(gitEvent.Category) % ContribRows;
            contribCells[(ContribColumns - 1) * ContribRows + row] += 1f;

            if (streamLabels.Count > 0)
            {
                streamQueue.Enqueue(Format(gitEvent));
                while (streamQueue.Count > 12)
                    streamQueue.Dequeue();
            }
        }

        void Update()
        {
            if (root == null)
                return;
            float dt = Time.deltaTime;
            refresh += dt;
            clockTick += dt;
            TickSeconds(dt);
            TickNumbers(dt);
            TickStage(dt);
            TickStream(dt);
            TickFades(dt);
            TickContrib(dt);
            TickBubbles(dt);
            TickTabs(dt);

            if (clockTick > 0.25f)
            {
                clockTick = 0f;
                string now = DateTime.Now.ToString("HH:mm:ss");
                if (clock != null)
                    clock.text = now;
                if (streamClock != null)
                    streamClock.text = now;
            }

            if (refresh > 0.5f)
            {
                refresh = 0f;
                RebuildTrending();
                RebuildRepoAxis();
                if (hotLanguage != null)
                    hotLanguage.text = dominantLanguage;
            }
        }

        void TickSeconds(float dt)
        {
            secondTimer += dt;
            while (secondTimer >= 1f)
            {
                secondTimer -= 1f;
                secondSlot = (secondSlot + 1) % addedPerSecond.Length;
                addedPerSecond[secondSlot] = 0;
                deletedPerSecond[secondSlot] = 0;
            }

            rateWindow += dt;
            if (rateWindow >= 2f)
            {
                rate = rateCount / rateWindow;
                rateWindow = 0f;
                rateCount = 0;
                if (streamRate != null)
                    streamRate.text = rate.ToString("0.0") + " ev/s";
            }
        }

        void TickNumbers(float dt)
        {
            shownForks = Approach(shownForks, forks, dt);
            if (!stageRevealOut)
                shownStage = Approach(shownStage, StageTarget(), dt);
            shownReviews = Approach(shownReviews, reviews, dt);
            WindowLines(activeTab, out double added, out double deleted);
            shownAdded = Approach(shownAdded, added, dt);
            shownDeleted = Approach(shownDeleted, deleted, dt);

            if (forksLabel != null)
                forksLabel.text = Mathf.RoundToInt(shownForks).ToString("N0");
            if (stageCount != null)
                stageCount.text = Gradient(Mathf.RoundToInt(shownStage).ToString("N0"));
            if (reviewCount != null)
                reviewCount.text = Mathf.RoundToInt(shownReviews).ToString("N0");
            if (linesAdded != null)
                linesAdded.text = Compact(shownAdded);
            if (linesDeleted != null)
                linesDeleted.text = "−" + Compact(shownDeleted);
        }

        void WindowLines(int tab, out double added, out double deleted)
        {
            switch (tab)
            {
                case 0:
                    added = 0;
                    deleted = 0;
                    for (int i = 0; i < addedPerSecond.Length; i++)
                    {
                        added += addedPerSecond[i];
                        deleted += deletedPerSecond[i];
                    }
                    break;
                case 1:
                    added = hourAdded;
                    deleted = hourDeleted;
                    break;
                case 2:
                    added = dayAdded;
                    deleted = dayDeleted;
                    break;
                default:
                    added = weekAdded;
                    deleted = weekDeleted;
                    break;
            }
        }

        static float Approach(float shown, float target, float dt)
        {
            float delta = Mathf.Abs(target - shown);
            float step = Mathf.Max(8f, delta * 2.4f) * dt;
            return Mathf.MoveTowards(shown, target, step);
        }

        static double Approach(double shown, double target, float dt)
        {
            double delta = Math.Abs(target - shown);
            double step = Math.Max(8.0, delta * 4.0) * dt;
            if (delta <= step)
                return target;
            return shown + Math.Sign(target - shown) * step;
        }

        int StageTarget()
        {
            if (stageIndex == 1 || stageIndex == 2)
                return activity;
            if (stageIndex == 3)
                return resolved;
            return collaborations;
        }

        void TickStage(float dt)
        {
            if (!stageRevealOut && stageReveal >= 1f)
                return;
            if (stageRevealOut)
            {
                stageReveal = Mathf.Max(0f, stageReveal - dt / 0.22f);
                if (stageReveal <= 0f && stageTitle != null)
                {
                    stageTitle.text = stageRevealTitle;
                    shownStage = StageTarget() * 0.92f;
                    stageRevealOut = false;
                }
            }
            else
            {
                stageReveal = Mathf.Min(1f, stageReveal + dt / 0.32f);
            }
            float eased = Mathf.SmoothStep(0f, 1f, stageReveal);
            if (stageTitle != null)
            {
                stageTitle.style.opacity = eased;
                stageTitle.style.translate = new Translate(0, (1f - eased) * 6f);
            }
            if (stageCount != null)
            {
                stageCount.style.opacity = eased;
                stageCount.style.translate = new Translate(0, (1f - eased) * 10f);
            }
        }

        // ---------------------------------------------------------------- stream

        void BuildStream()
        {
            if (stream == null)
                return;
            DateTime now = DateTime.UtcNow;
            for (int i = 0; i < StreamEntries; i++)
            {
                var label = new Label { enableRichText = true, pickingMode = PickingMode.Ignore };
                label.AddToClassList("stream-entry");
                GitEvent seeded = EventSimulator.Next();
                seeded.Time = now.AddSeconds(-i * 0.7);
                label.text = Format(seeded);
                stream.Add(label);
                streamLabels.Add(label);
            }
            filledStreamLines = streamLabels.Count;
        }

        void TickStream(float dt)
        {
            if (stream == null || streamLabels.Count == 0)
                return;
            streamCooldown -= dt;
            if (streamCooldown <= 0f && streamQueue.Count > 0)
            {
                // Newest on top: the oldest entry at the bottom is recycled to the top.
                int last = streamLabels.Count - 1;
                Label entry = streamLabels[last];
                streamLabels.RemoveAt(last);
                streamLabels.Insert(0, entry);
                entry.SendToBack();
                entry.text = streamQueue.Dequeue();
                streamSlide -= StreamEntryHeight;
                streamFresh = 0f;
                filledStreamLines = streamLabels.Count;
                // Drain faster when the queue backs up, but never faster than readable.
                streamCooldown = Mathf.Lerp(0.5f, 0.2f, Mathf.Clamp01(streamQueue.Count / 8f));
            }
            streamSlide = Mathf.MoveTowards(streamSlide, 0f, Mathf.Max(60f, -streamSlide * 7f) * dt);
            streamFresh = Mathf.Min(1f, streamFresh + dt / 0.4f);
            stream.style.translate = new Translate(0, streamSlide);

            // Entries dim with age and dissolve toward the bottom of the panel.
            float window = stream.parent != null ? stream.parent.resolvedStyle.height : 0f;
            if (float.IsNaN(window) || window <= 0f)
                window = StreamEntries * StreamEntryHeight;
            for (int i = 0; i < streamLabels.Count; i++)
            {
                float y = i * StreamEntryHeight + streamSlide;
                float depth = Mathf.Clamp01(y / window);
                float alpha = depth < 0.45f ? 1f : Mathf.Lerp(1f, 0f, (depth - 0.45f) / 0.5f);
                if (i == 0)
                    alpha *= streamFresh;
                streamLabels[i].style.opacity = alpha;
            }
        }

        static string Format(GitEvent gitEvent)
        {
            string kind = string.IsNullOrEmpty(gitEvent.Category) ? TypeLabel(gitEvent.Type) : gitEvent.Category;
            string hex = TypeHex(gitEvent);
            DateTime time = gitEvent.Time == default ? DateTime.UtcNow : gitEvent.Time;
            string actor = string.IsNullOrEmpty(gitEvent.Actor) ? "anonymous" : gitEvent.Actor;
            string subject = !string.IsNullOrEmpty(gitEvent.Repo) ? gitEvent.Repo : actor;
            int id = (Catalog.StableHash(actor + subject) ^ (int)(time.Ticks >> 12)) & 0x0fffffff;

            var text = new StringBuilder(220);
            text.Append("<color=#4b586b>").Append(time.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'")).Append("</color>\n");
            text.Append("<color=#56637a>").Append(id.ToString("x7")).Append(" · </color>");
            // Mark fills render nearly opaque, so use a deep tint of the hue behind bright text.
            ColorUtility.TryParseHtmlString("#" + hex, out Color ink);
            string shade = ColorUtility.ToHtmlStringRGB(Color.Lerp(new Color(0.05f, 0.06f, 0.08f), ink, 0.22f));
            text.Append("<mark=#").Append(shade).Append("ff><color=#").Append(hex).Append('>').Append(kind).Append("</color></mark>");
            text.Append("<color=#56637a> to </color><color=#c3cde0>").Append(Trim(subject, 26)).Append("</color>\n");
            text.Append("<color=#4f6f68>\"actor\": \"").Append(Trim(actor, 16)).Append('"');
            if (!string.IsNullOrEmpty(gitEvent.City))
                text.Append(", \"location\": \"").Append(gitEvent.City).Append('"');
            text.Append("</color>");
            return text.ToString();
        }

        static string TypeHex(GitEvent gitEvent)
        {
            if (gitEvent.Tint.a > 0.01f)
                return ColorUtility.ToHtmlStringRGB(gitEvent.Tint);
            switch (gitEvent.Type)
            {
                case GitEventType.Create: return "ff6b4a";
                case GitEventType.Push: return "4f7dff";
                case GitEventType.Merge: return "2fd27a";
                case GitEventType.PullRequest: return "ff9640";
                case GitEventType.Review: return "f2c94c";
                case GitEventType.Fork: return "b98cff";
                case GitEventType.Delete: return "ff4d6d";
                default: return "8fa3c0";
            }
        }

        static string TypeLabel(GitEventType type)
        {
            switch (type)
            {
                case GitEventType.Push: return "PushEvent";
                case GitEventType.PullRequest: return "PullRequest";
                case GitEventType.Fork: return "ForkEvent";
                case GitEventType.Create: return "CreateEvent";
                case GitEventType.Merge: return "MergeEvent";
                case GitEventType.Delete: return "DeleteEvent";
                case GitEventType.Review: return "ReviewEvent";
                default: return type.ToString();
            }
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

        // ---------------------------------------------------------------- stage toggles and tabs

        public void SetStage(int index, string title)
        {
            stageIndex = index;
            if (stageTitle != null && stageTitle.text != title && stageRevealTitle != title)
            {
                stageRevealTitle = title;
                stageRevealOut = true;
            }
            for (int i = 0; i < toggles.Length; i++)
            {
                if (toggles[i] == null)
                    continue;
                if (i == index)
                    toggles[i].AddToClassList("toggle-on");
                else
                    toggles[i].RemoveFromClassList("toggle-on");
            }
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
                button.pickingMode = PickingMode.Position;
                button.clicked += () => StageChosen?.Invoke(index);
            }
        }

        void BindTabs()
        {
            for (int i = 0; i < tabs.Length; i++)
            {
                int index = i;
                Button button = root.Q<Button>("tab-" + i);
                tabs[i] = button;
                if (button == null)
                    continue;
                button.focusable = false;
                button.pickingMode = PickingMode.Position;
                button.clicked += () =>
                {
                    tabHeld = 30f;
                    SelectTab(index, true);
                };
            }
        }

        void SelectTab(int index, bool animate)
        {
            activeTab = Mathf.Clamp(index, 0, TabNames.Length - 1);
            tabTimer = 0f;
            for (int i = 0; i < tabs.Length; i++)
            {
                if (tabs[i] == null)
                    continue;
                if (i == activeTab)
                    tabs[i].AddToClassList("tab-on");
                else
                    tabs[i].RemoveFromClassList("tab-on");
            }
            if (!animate)
            {
                WindowLines(activeTab, out shownAdded, out shownDeleted);
                return;
            }
            // Count up from a fraction of the new figure, like the reference overlay.
            WindowLines(activeTab, out double added, out double deleted);
            shownAdded = added * 0.82;
            shownDeleted = deleted * 0.82;
        }

        void TickTabs(float dt)
        {
            if (tabHeld > 0f)
            {
                tabHeld -= dt;
                return;
            }
            tabTimer += dt;
            if (tabTimer > 7f)
                SelectTab((activeTab + 1) % TabNames.Length, true);
        }

        public void SetCycle(float secondsLeft, bool paused)
        {
            if (cycleLabel != null)
                cycleLabel.text = paused ? "held" : Mathf.CeilToInt(Mathf.Max(0f, secondsLeft)) + " sec";
        }

        // Per-digit blend from periwinkle to rose, like the reference's headline figure.
        static readonly Color GradientFrom = new Color(0.56f, 0.62f, 1f);
        static readonly Color GradientTo = new Color(1f, 0.56f, 0.7f);
        readonly StringBuilder gradientText = new StringBuilder(160);

        string Gradient(string value)
        {
            gradientText.Clear();
            int count = Mathf.Max(1, value.Length - 1);
            for (int i = 0; i < value.Length; i++)
            {
                Color color = Color.Lerp(GradientFrom, GradientTo, i / (float)count);
                gradientText.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(color)).Append('>').Append(value[i]).Append("</color>");
            }
            return gradientText.ToString();
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

        // ---------------------------------------------------------------- trending

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

            var key = new StringBuilder();
            int listed = Mathf.Min(10, ranking.Count);
            for (int i = 0; i < listed; i++)
                key.Append(ranking[i].Name).Append('|');
            string nextKey = key.ToString();
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

                var rank = new Label((i + 1).ToString()) { pickingMode = PickingMode.Ignore };
                rank.AddToClassList("trend-rank");
                row.Add(rank);
                row.Add(Avatar(stat.Name, Palette.ForLanguage(stat.Language), "avatar", 30f));

                var text = new VisualElement { pickingMode = PickingMode.Ignore };
                text.AddToClassList("trend-text");
                var name = new Label(Trim(stat.Name, 18)) { pickingMode = PickingMode.Ignore };
                name.AddToClassList("trend-name");
                var stats = new Label(Stats(stat)) { pickingMode = PickingMode.Ignore };
                stats.AddToClassList("trend-stats");
                text.Add(name);
                text.Add(stats);
                row.Add(text);

                var repo = new Label(Trim(ShortRepo(stat.Repo), 18)) { pickingMode = PickingMode.Ignore };
                repo.AddToClassList("trend-repo");
                row.Add(repo);

                // Lower ranks recede, as in the reference list.
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

        static string Stats(ActorStat stat)
        {
            string language = string.IsNullOrEmpty(stat.Language) ? "" : "   " + stat.Language;
            return "↑ " + stat.Count.ToString("N0") + language;
        }

        VisualElement Avatar(string name, Color color, string avatarClass, float size)
        {
            var avatar = new VisualElement { pickingMode = PickingMode.Ignore };
            avatar.AddToClassList(avatarClass);
            avatar.style.backgroundImage = new StyleBackground(Face(name, color));
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

        Texture2D Face(string name, Color color)
        {
            if (string.IsNullOrEmpty(name))
                name = "?";
            if (faces.TryGetValue(name, out Texture2D cached))
                return cached;

            const int size = 48;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[size * size];
            int hash = Catalog.StableHash(name);
            Color disc = Color.HSVToRGB((hash % 360) / 360f, 0.55f, 0.85f);
            disc = Color.Lerp(disc, color, 0.35f);
            Color mark = Color.Lerp(disc, Color.white, 0.55f);
            // A 5x5 mirrored identicon, so every developer gets a distinct face.
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int cx = Mathf.Clamp((x - 6) / 7, 0, 4);
                    int cy = Mathf.Clamp((y - 6) / 7, 0, 4);
                    int mirrored = cx > 2 ? 4 - cx : cx;
                    bool inside = x >= 6 && x < 41 && y >= 6 && y < 41;
                    bool on = inside && ((hash >> (mirrored * 5 + cy)) & 1) == 1;
                    pixels[y * size + x] = on ? mark : disc;
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false);
            faces[name] = texture;
            return texture;
        }

        // ---------------------------------------------------------------- reviewers timeline

        void SeedBubbles()
        {
            string[] names = { "orbit-lane", "northwind", "keel", "paperkite", "glassfork", "halcyon", "lattice", "harbor", "inkwell", "cedar", "lowtide", "kindling", "overpass", "byteferry" };
            string[] languages = { "TypeScript", "Python", "Rust", "Go", "JavaScript", "C#", "Swift", "Ruby", "Kotlin", "Java", "Go", "Rust", "Python", "C++" };
            for (int i = 0; i < names.Length; i++)
                AddBubble(names[i], Palette.ForLanguage(languages[i]), UnityEngine.Random.Range(10, 600), UnityEngine.Random.Range(0f, ReviewWindow * 0.95f));
            for (int i = 0; i < 4 && i < Catalog.Repos.Length; i++)
                RememberRepo(ShortRepo(Catalog.Repos[i * 3 % Catalog.Repos.Length].Name));
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
            Color.RGBToHSV(color, out float hue, out float saturation, out float value);
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
            float axis = 22f;
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
            if (recentRepos.Count > 4)
                recentRepos.RemoveAt(recentRepos.Count - 1);
        }

        void RebuildRepoAxis()
        {
            if (reviewRepos == null)
                return;
            while (reviewRepos.childCount < 4)
            {
                var label = new Label { pickingMode = PickingMode.Ignore };
                label.AddToClassList("review-repo");
                reviewRepos.Add(label);
            }
            // Oldest on the left, newest on the right, matching the timeline direction.
            for (int i = 0; i < 4; i++)
            {
                int source = 3 - i;
                ((Label)reviewRepos[i]).text = source < recentRepos.Count ? Trim(recentRepos[source], 14) : "";
            }
        }

        // ---------------------------------------------------------------- contribution graph

        void BuildContrib()
        {
            for (int c = 0; c < ContribColumns; c++)
            {
                for (int r = 0; r < ContribRows; r++)
                {
                    float wave = Mathf.PerlinNoise(c * 0.21f, r * 0.53f);
                    contribCells[c * ContribRows + r] = wave * wave * 40f * RowWeight(r);
                }
            }
            for (int r = 0; r < ContribRows; r++)
                contribPeak[r] = 30f * RowWeight(r);

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
            // Matches the demo's event mix so seeded history looks like live history.
            float[] weights = { 1f, 0.45f, 0.25f, 0.3f, 0.18f, 0.12f, 0.3f };
            return weights[Mathf.Clamp(row, 0, weights.Length - 1)];
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
                contribTotal.text = Mathf.RoundToInt(total).ToString("N0") + " events";
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

        // ---------------------------------------------------------------- helpers

        void SeedChrome()
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

        static Texture2D BuildGrid(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point
            };
            var pixels = new Color32[size * size];
            var line = new Color32(160, 180, 220, 4);
            var cross = new Color32(170, 190, 230, 12);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool onLine = x == 0 || y == 0;
                    bool onCross = (x == 0 && y < 3) || (y == 0 && x < 3) || (x == 0 && y > size - 3) || (y == 0 && x > size - 3);
                    pixels[y * size + x] = onCross ? cross : onLine ? line : new Color32(0, 0, 0, 0);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false);
            return texture;
        }

        static string Compact(double value)
        {
            if (value >= 1e9)
                return (value / 1e6).ToString("N0") + "m";
            if (value >= 1e6)
                return (value / 1e3).ToString("N0") + "k";
            return value.ToString("N0");
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
                return value ?? "";
            return value.Substring(0, max - 1) + "…";
        }
    }
}
