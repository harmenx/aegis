using UnityEngine;

namespace Gitmos
{
    [DefaultExecutionOrder(-20)]
    public class Metronome : MonoBehaviour
    {
        public StageBase[] stages;
        public DashboardController dashboard;
        public float holdSeconds = 18f;
        public float fadeSeconds = 1.5f;

        public int CurrentIndex { get; private set; }
        public int VisibleIndex { get; private set; }
        public bool Paused { get; private set; }

        static readonly string[] Titles =
        {
            "Collaborations Today",
            "Activity Today",
            "Activity Today",
            "Threats Resolved"
        };

        int incoming = -1;
        float fade;
        float hold;
        float fadeDuration = 1.5f;

        void Awake()
        {
            Application.runInBackground = true;
        }

        void OnEnable()
        {
            if (dashboard != null)
                dashboard.StageChosen += Jump;
        }

        void OnDisable()
        {
            if (dashboard != null)
                dashboard.StageChosen -= Jump;
        }

        void Start()
        {
            if (dashboard != null)
            {
                dashboard.StageChosen -= Jump;
                dashboard.StageChosen += Jump;
            }
            Apply();
        }

        void Update()
        {
            if (stages == null || stages.Length == 0)
                return;

            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
                Jump(0);
            else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
                Jump(1);
            else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
                Jump(2);
            else if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4))
                Jump(3);
            else if (Input.GetKeyDown(KeyCode.Space))
                Paused = !Paused;

            if (incoming < 0)
            {
                if (!Paused)
                {
                    hold += Time.deltaTime;
                    if (hold >= holdSeconds)
                        Begin((CurrentIndex + 1) % stages.Length, fadeSeconds);
                }
            }
            else
            {
                fade += Time.deltaTime / Mathf.Max(0.05f, fadeDuration);
                if (fade >= 1f)
                {
                    CurrentIndex = incoming;
                    incoming = -1;
                    fade = 0f;
                    hold = 0f;
                }
            }

            Apply();
            if (dashboard != null)
                dashboard.SetStage(VisibleIndex, Titles[Mathf.Clamp(VisibleIndex, 0, Titles.Length - 1)]);
        }

        public void Jump(int index)
        {
            if (stages == null || index < 0 || index >= stages.Length)
                return;
            Paused = true;
            hold = 0f;
            if (incoming < 0 && index == CurrentIndex)
                return;
            Begin(index, 0.7f);
        }

        void Begin(int index, float duration)
        {
            incoming = index;
            fade = 0f;
            fadeDuration = duration;
        }

        void Apply()
        {
            VisibleIndex = incoming >= 0 && fade >= 0.5f ? incoming : CurrentIndex;
            for (int i = 0; i < stages.Length; i++)
            {
                if (stages[i] == null)
                    continue;
                float alpha = 0f;
                if (i == CurrentIndex)
                    alpha = incoming < 0 ? 1f : 1f - Mathf.Clamp01(fade);
                if (i == incoming)
                    alpha = Mathf.Clamp01(fade);
                stages[i].SetAlpha(alpha);
            }
        }
    }
}
