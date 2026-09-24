using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Gitmos
{
    public class GitHubEventsClient : MonoBehaviour
    {
        [SerializeField] float intervalSeconds = 8f;
        [SerializeField] bool enabledFeed = true;

        readonly HashSet<string> seen = new HashSet<string>();
        bool loggedFailure;

        void Start()
        {
            if (enabledFeed)
                StartCoroutine(Poll());
        }

        IEnumerator Poll()
        {
            while (enabled)
            {
                if (EventSimulator.DemoEnabled)
                    yield return FetchOnce();
                yield return new WaitForSeconds(intervalSeconds);
            }
        }

        IEnumerator FetchOnce()
        {
            using (var request = UnityWebRequest.Get("https://api.github.com/events?per_page=30"))
            {
                request.SetRequestHeader("User-Agent", "gitmos-rebuild");
                request.SetRequestHeader("Accept", "application/vnd.github+json");
                string token = ReadToken();
                if (!string.IsNullOrEmpty(token))
                    request.SetRequestHeader("Authorization", "Bearer " + token);

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    if (!loggedFailure)
                    {
                        loggedFailure = true;
                        Debug.Log("GitHub events feed unavailable, simulation continues. " + request.error);
                    }
                    yield break;
                }

                try
                {
                    Parse(request.downloadHandler.text);
                }
                catch (Exception error)
                {
                    if (!loggedFailure)
                    {
                        loggedFailure = true;
                        Debug.Log("GitHub events feed could not be read, simulation continues. " + error.Message);
                    }
                }
            }
        }

        void Parse(string json)
        {
            if (string.IsNullOrEmpty(json) || json[0] != '[')
                return;

            int search = 0;
            int emitted = 0;
            while (emitted < 20)
            {
                int typeAt = json.IndexOf("\"type\":\"", search, StringComparison.Ordinal);
                if (typeAt < 0)
                    break;

                int typeStart = typeAt + 8;
                int typeEnd = json.IndexOf('"', typeStart);
                if (typeEnd < 0)
                    break;

                string typeName = json.Substring(typeStart, typeEnd - typeStart);
                int next = json.IndexOf("\"type\":\"", typeEnd, StringComparison.Ordinal);
                if (next < 0)
                    next = json.Length;

                int headerStart = Mathf.Max(0, typeAt - 48);
                string id = Extract(json.Substring(headerStart, typeAt - headerStart), "\"id\":\"");
                string window = json.Substring(typeEnd, Mathf.Min(next - typeEnd, 1800));
                search = next;
                if (!string.IsNullOrEmpty(id) && !seen.Add(id))
                    continue;
                if (seen.Count > 400)
                    seen.Clear();

                if (!TryMap(typeName, window.Contains("\"merged\":true"), out GitEventType type))
                    continue;

                string actor = Extract(window, "\"login\":\"");
                string repo = Extract(window, "\"name\":\"");
                if (string.IsNullOrEmpty(actor) || string.IsNullOrEmpty(repo))
                    continue;

                string language = Extract(window, "\"language\":\"");
                if (string.IsNullOrEmpty(language))
                    language = LanguageFor(repo);

                int cityIndex = Catalog.CityIndexFor(actor);
                City city = Catalog.Cities[cityIndex];
                EventBus.Publish(new GitEvent
                {
                    Time = DateTime.UtcNow,
                    Type = type,
                    Actor = actor,
                    Repo = repo,
                    Language = language,
                    Additions = UnityEngine.Random.Range(1, 240),
                    Deletions = UnityEngine.Random.Range(0, 40),
                    Latitude = city.Latitude,
                    Longitude = city.Longitude,
                    City = city.Name,
                    CityIndex = cityIndex
                });
                emitted++;
            }
        }

        static bool TryMap(string typeName, bool merged, out GitEventType type)
        {
            switch (typeName)
            {
                case "PushEvent":
                    type = GitEventType.Push;
                    return true;
                case "PullRequestEvent":
                    type = merged ? GitEventType.Merge : GitEventType.PullRequest;
                    return true;
                case "ForkEvent":
                    type = GitEventType.Fork;
                    return true;
                case "CreateEvent":
                    type = GitEventType.Create;
                    return true;
                case "DeleteEvent":
                    type = GitEventType.Delete;
                    return true;
                case "PullRequestReviewEvent":
                case "PullRequestReviewCommentEvent":
                case "IssueCommentEvent":
                    type = GitEventType.Review;
                    return true;
                default:
                    type = GitEventType.Push;
                    return false;
            }
        }

        static string LanguageFor(string repo)
        {
            for (int i = 0; i < Catalog.Repos.Length; i++)
            {
                if (Catalog.Repos[i].Name == repo)
                    return Catalog.Repos[i].Language;
            }
            string[] languages = { "TypeScript", "Python", "Go", "Rust", "JavaScript", "C#" };
            return languages[Catalog.StableHash(repo) % languages.Length];
        }

        static string Extract(string source, string key)
        {
            int index = source.IndexOf(key, StringComparison.Ordinal);
            if (index < 0)
                return null;
            index += key.Length;
            int end = source.IndexOf('"', index);
            if (end < 0)
                return null;
            return source.Substring(index, end - index);
        }

        static string ReadToken()
        {
            try
            {
                string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "github.token"));
                if (!File.Exists(path))
                    return null;
                return File.ReadAllText(path).Trim();
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
