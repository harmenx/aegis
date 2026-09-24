using UnityEngine;

namespace Gitmos
{
    public class EventSimulator : MonoBehaviour
    {
        [SerializeField] float eventsPerSecond = 14f;

        public static bool DemoEnabled = true;

        float debt;

        void Update()
        {
            if (!DemoEnabled)
                return;
            debt += Mathf.Max(1f, eventsPerSecond) * Time.deltaTime;
            int burst = Mathf.FloorToInt(debt);
            if (burst > 8)
                burst = 8;
            debt -= burst;
            for (int i = 0; i < burst; i++)
                EventBus.Publish(Next());
        }

        public static GitEvent Next()
        {
            string actor = Catalog.Actors[Random.Range(0, Catalog.Actors.Length)];
            RepoInfo repo = Catalog.Repos[Random.Range(0, Catalog.Repos.Length)];
            int cityIndex = Catalog.CityIndexFor(actor);
            City city = Catalog.Cities[cityIndex];

            return new GitEvent
            {
                Time = System.DateTime.UtcNow,
                Type = RandomType(),
                Actor = actor,
                Repo = repo.Name,
                Language = repo.Language,
                Additions = Random.Range(4, 420),
                Deletions = Random.Range(0, 90),
                Latitude = city.Latitude,
                Longitude = city.Longitude,
                City = city.Name,
                CityIndex = cityIndex
            };
        }

        static GitEventType RandomType()
        {
            int roll = Random.Range(0, 100);
            if (roll < 42) return GitEventType.Push;
            if (roll < 60) return GitEventType.PullRequest;
            if (roll < 72) return GitEventType.Review;
            if (roll < 82) return GitEventType.Create;
            if (roll < 90) return GitEventType.Fork;
            if (roll < 96) return GitEventType.Merge;
            return GitEventType.Delete;
        }
    }
}
