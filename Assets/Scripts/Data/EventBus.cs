using System;

namespace Gitmos
{
    public static class EventBus
    {
        public static event Action<GitEvent> OnEvent;

        public static int Published { get; private set; }

        public static void Publish(GitEvent gitEvent)
        {
            Published++;
            OnEvent?.Invoke(gitEvent);
        }

        public static void ResetCounts()
        {
            Published = 0;
        }
    }
}
