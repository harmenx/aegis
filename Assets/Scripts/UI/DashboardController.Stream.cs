using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Aegis
{
    // Data Stream panel: newest entry on top, older entries dim and dissolve downward.
    public partial class DashboardController
    {
        const int StreamEntries = 24;
        const float StreamEntryHeight = 54f;
        const int StreamBacklog = 12;

        Label streamClock;
        Label streamRate;
        VisualElement stream;
        readonly List<Label> streamLabels = new List<Label>(StreamEntries);
        readonly Queue<string> streamQueue = new Queue<string>();
        int filledStreamLines;
        float streamSlide;
        float streamFresh = 1f;
        float streamCooldown;
        float rateWindow;
        int rateCount;
        float rate;

        void BuildStream()
        {
            if (stream == null)
                return;
            DateTime now = DateTime.UtcNow;
            for (int i = 0; i < StreamEntries; i++)
            {
                var label = new Label { enableRichText = true, pickingMode = PickingMode.Ignore };
                label.AddToClassList("stream-entry");
                if (seeded)
                {
                    GitEvent sample = EventSimulator.Next();
                    sample.Time = now.AddSeconds(-i * 0.7);
                    label.text = Format(sample);
                }
                stream.Add(label);
                streamLabels.Add(label);
            }
            filledStreamLines = streamLabels.Count;
        }

        void QueueStream(GitEvent gitEvent)
        {
            if (streamLabels.Count == 0)
                return;
            streamQueue.Enqueue(Format(gitEvent));
            while (streamQueue.Count > StreamBacklog)
                streamQueue.Dequeue();
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

        // Everything that came from outside is escaped before it is placed in the rich-text entry.
        internal static string Format(GitEvent gitEvent)
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
            text.Append("<mark=#").Append(shade).Append("ff><color=#").Append(hex).Append('>')
                .Append(TextFormat.EscapeRichText(TextFormat.Ellipsize(kind, 18))).Append("</color></mark>");
            text.Append("<color=#56637a> to </color><color=#c3cde0>")
                .Append(TextFormat.EscapeRichText(TextFormat.Ellipsize(subject, 26))).Append("</color>\n");
            text.Append("<color=#4f6f68>\"actor\": \"").Append(TextFormat.EscapeRichText(TextFormat.Ellipsize(actor, 16))).Append('"');
            if (!string.IsNullOrEmpty(gitEvent.City))
                text.Append(", \"location\": \"").Append(TextFormat.EscapeRichText(TextFormat.Ellipsize(gitEvent.City, 18))).Append('"');
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
    }
}
