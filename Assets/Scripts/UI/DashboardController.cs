using System;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Aegis
{
    // Drives the UI Toolkit overlay. Panels live in partial files beside this one:
    // Stream, Trending, Reviewers, and Contribution.
    public partial class DashboardController : MonoBehaviour
    {
        public Font monoFont;
        public PanelSettings panelSettings;

        public int ForkedProjects => forks;
        public int StreamLines => filledStreamLines;
        public string StageTitle => stageTitle != null ? stageTitle.text : "";
        public event Action<int> StageChosen;

        static readonly string[] TabNames = { "Min", "Hour", "Day", "Week" };

        // A custom point newer than this keeps the status on Live.
        const float LiveWindowSeconds = 15f;

        UIDocument document;
        VisualElement root;
        Label forksLabel;
        Label stageTitle;
        Label stageCount;
        Label clock;
        Label linesAdded;
        Label linesDeleted;
        Label cycleLabel;
        Label statusLabel;
        VisualElement statusPill;
        VisualElement emptyState;
        Label emptyHint;
        readonly Button[] toggles = new Button[4];
        readonly Button[] tabs = new Button[4];

        readonly double[] addedPerSecond = new double[60];
        readonly double[] deletedPerSecond = new double[60];
        Texture2D vignetteTexture;
        Texture2D gridTexture;

        // Demo baselines, so the demo opens on a busy lobby. Cleared when the demo is off.
        int forks = 418;
        int collaborations = 1358;
        int activity = 18640;
        int reviews = 3286;
        int resolved = 96;
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

        bool seeded;
        int stageIndex;
        int activeTab = 3;
        int secondSlot;
        float refresh;
        float clockTick;
        float secondTimer;
        float tabTimer;
        float tabHeld;
        float stageReveal = 1f;
        bool stageRevealOut;
        string stageRevealTitle = "";
        string statusKey = "";

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
            DashboardTextures.Destroy(contribTexture);
            DashboardTextures.Destroy(vignetteTexture);
            DashboardTextures.Destroy(gridTexture);
            foreach (Texture2D face in faces.Values)
                DashboardTextures.Destroy(face);
        }

        void Bind()
        {
            if (document == null || document.rootVisualElement == null)
            {
                Log.Warn("Dashboard has no visual tree; the overlay is disabled.");
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
            statusLabel = root.Q<Label>("feed-status");
            statusPill = root.Q<VisualElement>("feed-pill");
            emptyState = root.Q<VisualElement>("empty-state");
            emptyHint = root.Q<Label>("empty-hint");
            trending = root.Q<VisualElement>("trending");
            stream = root.Q<VisualElement>("stream");
            reviewers = root.Q<VisualElement>("reviewers");
            reviewRepos = root.Q<VisualElement>("review-repos");
            contrib = root.Q<VisualElement>("contrib");
            if (hotLanguage != null)
                hotLanguage.enableRichText = false;
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
                gridTexture = DashboardTextures.Grid(48);
                grid.style.backgroundImage = new StyleBackground(gridTexture);
                grid.style.backgroundRepeat = new StyleBackgroundRepeat(new BackgroundRepeat(Repeat.Repeat, Repeat.Repeat));
                grid.style.backgroundSize = new StyleBackgroundSize(new BackgroundSize(new Length(48f), new Length(48f)));
            }

            var vignette = root.Q<VisualElement>("vignette");
            if (vignette != null)
            {
                vignetteTexture = DashboardTextures.Vignette(256);
                vignette.style.backgroundImage = new StyleBackground(vignetteTexture);
            }

            // Real data should never sit next to invented totals: only the demo gets a head start.
            seeded = EventSimulator.DemoEnabled;
            if (seeded)
                SeedTrending();
            else
                ClearBaselines();

            BuildStream();
            BuildContrib();
            if (seeded)
                SeedBubbles();
            SelectTab(activeTab, false);
            RebuildTrending();
            RebuildRepoAxis();
            TickStatus();
        }

        void ClearBaselines()
        {
            forks = collaborations = activity = reviews = resolved = 0;
            hourAdded = hourDeleted = dayAdded = dayDeleted = weekAdded = weekDeleted = 0;
            shownAdded = shownDeleted = 0;
            shownForks = shownStage = shownReviews = 0f;
            dominantLanguage = "";
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
                OnReview(gitEvent, added);
            }

            CountActor(gitEvent);
            CountContribution(gitEvent);
            QueueStream(gitEvent);
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
                TickStatus();
            }

            if (refresh > 0.5f)
            {
                refresh = 0f;
                RebuildTrending();
                RebuildRepoAxis();
                if (hotLanguage != null)
                {
                    hotLanguage.text = dominantLanguage;
                    hotLanguage.style.display = string.IsNullOrEmpty(dominantLanguage) ? DisplayStyle.None : DisplayStyle.Flex;
                }
            }
        }

        // ---------------------------------------------------------------- feed status

        void TickStatus()
        {
            bool live = FeedStatus.SecondsSinceCustomPoint < LiveWindowSeconds;
            bool demo = EventSimulator.DemoEnabled;
            bool failed = FeedStatus.Listen == ListenState.Failed;

            string state;
            string text;
            if (live)
            {
                state = "live";
                text = demo ? "Live + demo" : "Live";
            }
            else if (demo)
            {
                state = "demo";
                text = "Demo data";
            }
            else
            {
                state = failed ? "error" : "idle";
                text = FeedStatus.Accepted > 0 ? "Idle" : "Waiting for data";
            }
            if (FeedStatus.Listen == ListenState.Listening)
                text += " · " + FeedStatus.ListenDetail;
            else if (failed)
                text += "  · port " + AegisConfig.Current.port + " unavailable";

            string key = state + text;
            if (key != statusKey)
            {
                statusKey = key;
                if (statusLabel != null)
                    statusLabel.text = text;
                if (statusPill != null)
                {
                    statusPill.EnableInClassList("status-live", state == "live");
                    statusPill.EnableInClassList("status-demo", state == "demo");
                    statusPill.EnableInClassList("status-idle", state == "idle");
                    statusPill.EnableInClassList("status-error", state == "error");
                }
            }

            bool empty = !demo && FeedStatus.Accepted == 0;
            if (emptyState != null)
                emptyState.style.display = empty ? DisplayStyle.Flex : DisplayStyle.None;
            if (empty && emptyHint != null)
            {
                AegisConfig config = AegisConfig.Current;
                emptyHint.text = FeedStatus.Listen == ListenState.Listening
                    ? "POST JSON to " + config.Endpoint + "\nor write points.json next to the app."
                    : "Write points.json or points.jsonl next to the app.\n" + (failed
                        ? "HTTP on port " + config.port + " is unavailable (" + FeedStatus.ListenDetail + ")."
                        : "HTTP intake is off.");
            }
        }

        // ---------------------------------------------------------------- counters

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
                forksLabel.text = TextFormat.Count(Mathf.RoundToInt(shownForks));
            if (stageCount != null)
                stageCount.text = Gradient(TextFormat.Count(Mathf.RoundToInt(shownStage)));
            if (reviewCount != null)
                reviewCount.text = TextFormat.Count(Mathf.RoundToInt(shownReviews));
            if (linesAdded != null)
                linesAdded.text = TextFormat.Compact(shownAdded);
            if (linesDeleted != null)
                linesDeleted.text = shownDeleted < 0.5 ? "0" : "−" + TextFormat.Compact(shownDeleted);
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

        // ---------------------------------------------------------------- stage title

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

        // Per-digit blend from periwinkle to rose for the headline figure.
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
                if (toggles[i] != null)
                    toggles[i].EnableInClassList("toggle-on", i == index);
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
                if (tabs[i] != null)
                    tabs[i].EnableInClassList("tab-on", i == activeTab);
            }
            if (!animate)
            {
                WindowLines(activeTab, out shownAdded, out shownDeleted);
                return;
            }
            // Count up from a fraction of the new figure.
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
    }
}
