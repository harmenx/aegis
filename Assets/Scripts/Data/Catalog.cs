using UnityEngine;

namespace Gitmos
{
    public struct City
    {
        public string Name;
        public float Latitude;
        public float Longitude;

        public City(string name, float latitude, float longitude)
        {
            Name = name;
            Latitude = latitude;
            Longitude = longitude;
        }
    }

    public struct RepoInfo
    {
        public string Name;
        public string Language;

        public RepoInfo(string name, string language)
        {
            Name = name;
            Language = language;
        }
    }

    public static class Catalog
    {
        public static readonly string[] Actors =
        {
            "orbit-lane", "northwind", "keel", "paperkite", "sublayer", "marrow",
            "fieldnote", "glassfork", "lintdept", "quietpush", "vectorina", "halcyon",
            "lowtide", "patchwork", "signalroom", "byteferry", "kindling", "overpass",
            "smallorbit", "cedar", "nightbuild", "lattice", "warmcache", "openkeel",
            "driftmail", "secondlight", "branchline", "softclock", "harbor", "inkwell"
        };

        public static readonly RepoInfo[] Repos =
        {
            new RepoInfo("facebook/react", "JavaScript"),
            new RepoInfo("vuejs/core", "TypeScript"),
            new RepoInfo("tensorflow/tensorflow", "Python"),
            new RepoInfo("golang/go", "Go"),
            new RepoInfo("rust-lang/rust", "Rust"),
            new RepoInfo("dotnet/runtime", "C#"),
            new RepoInfo("kubernetes/kubernetes", "Go"),
            new RepoInfo("microsoft/vscode", "TypeScript"),
            new RepoInfo("godotengine/godot", "C++"),
            new RepoInfo("neovim/neovim", "C"),
            new RepoInfo("denoland/deno", "Rust"),
            new RepoInfo("oven-sh/bun", "Zig"),
            new RepoInfo("nixos/nixpkgs", "Nix"),
            new RepoInfo("home-assistant/core", "Python"),
            new RepoInfo("supabase/supabase", "TypeScript"),
            new RepoInfo("tauri-apps/tauri", "Rust"),
            new RepoInfo("withastro/astro", "TypeScript"),
            new RepoInfo("sveltejs/svelte", "JavaScript"),
            new RepoInfo("prisma/prisma", "TypeScript"),
            new RepoInfo("torvalds/linux", "C"),
            new RepoInfo("django/django", "Python"),
            new RepoInfo("rails/rails", "Ruby"),
            new RepoInfo("spring-projects/spring-boot", "Java"),
            new RepoInfo("apple/swift", "Swift"),
            new RepoInfo("JetBrains/kotlin", "Kotlin")
        };

        public static readonly City[] Cities =
        {
            new City("San Francisco", 37.77f, -122.42f),
            new City("New York", 40.71f, -74.01f),
            new City("London", 51.51f, -0.13f),
            new City("Berlin", 52.52f, 13.40f),
            new City("Paris", 48.86f, 2.35f),
            new City("Tokyo", 35.68f, 139.69f),
            new City("Seoul", 37.57f, 126.98f),
            new City("Singapore", 1.35f, 103.82f),
            new City("Bengaluru", 12.97f, 77.59f),
            new City("Sydney", -33.87f, 151.21f),
            new City("São Paulo", -23.55f, -46.63f),
            new City("Buenos Aires", -34.60f, -58.38f),
            new City("Toronto", 43.65f, -79.38f),
            new City("Amsterdam", 52.37f, 4.90f),
            new City("Stockholm", 59.33f, 18.07f),
            new City("Lagos", 6.52f, 3.38f),
            new City("Nairobi", -1.29f, 36.82f),
            new City("Cairo", 30.04f, 31.24f),
            new City("Dubai", 25.20f, 55.27f),
            new City("Shanghai", 31.23f, 121.47f),
            new City("Beijing", 39.90f, 116.41f),
            new City("Taipei", 25.03f, 121.57f),
            new City("Melbourne", -37.81f, 144.96f),
            new City("Mexico City", 19.43f, -99.13f),
            new City("Chicago", 41.88f, -87.63f),
            new City("Seattle", 47.61f, -122.33f),
            new City("Austin", 30.27f, -97.74f),
            new City("Dublin", 53.35f, -6.26f),
            new City("Zürich", 47.38f, 8.54f),
            new City("Tel Aviv", 32.09f, 34.78f),
            new City("Warsaw", 52.23f, 21.01f),
            new City("Helsinki", 60.17f, 24.94f),
            new City("Montreal", 45.50f, -73.57f),
            new City("Vancouver", 49.28f, -123.12f),
            new City("Johannesburg", -26.20f, 28.05f),
            new City("Mumbai", 19.08f, 72.88f),
            new City("Jakarta", -6.21f, 106.85f),
            new City("Bangkok", 13.76f, 100.50f),
            new City("Auckland", -36.85f, 174.76f),
            new City("Reykjavík", 64.15f, -21.94f)
        };

        public static int StableHash(string value)
        {
            unchecked
            {
                int hash = 23;
                if (string.IsNullOrEmpty(value))
                    return hash;
                for (int i = 0; i < value.Length; i++)
                    hash = hash * 31 + value[i];
                return hash & 0x7fffffff;
            }
        }

        public static int CityIndexFor(string actor)
        {
            if (Cities.Length == 0)
                return 0;
            return StableHash(actor) % Cities.Length;
        }

        public static Vector3 Direction(float latitude, float longitude)
        {
            float lat = latitude * Mathf.Deg2Rad;
            float lon = longitude * Mathf.Deg2Rad;
            return new Vector3(
                Mathf.Cos(lat) * Mathf.Sin(lon),
                Mathf.Sin(lat),
                Mathf.Cos(lat) * Mathf.Cos(lon));
        }
    }
}
