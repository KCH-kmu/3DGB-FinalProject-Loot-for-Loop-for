using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-150)]
public sealed class RaidRouteDirector : MonoBehaviour
{
    [SerializeField] RaidRoutePoint[] points = Array.Empty<RaidRoutePoint>();
    [SerializeField] string hideoutScene = "Hideout_Bunker";
    [SerializeField, Min(.1f)] float extractionSeconds = 3;
    public IReadOnlyList<RaidRoutePoint> Points => points;
    public int SpawnIndex { get; private set; } = -1;
    public int[] ExitIndices { get; private set; } = Array.Empty<int>();
    public RaidRoutePoint SpawnPoint => SpawnIndex >= 0 ? points[SpawnIndex] : null;
    public RaidRoutePoint CurrentExtraction { get; private set; }
    public float ExtractionProgress { get; private set; }
    public float ExtractionSeconds => extractionSeconds;
    public bool IsExtracting { get; private set; }
    TopDownPlayer player;
    PlayerInventory inventory;
    GUIStyle heading, label;
    string travelError;

    public void Configure(RaidRoutePoint[] routePoints)
    {
        if (Application.isPlaying) throw new InvalidOperationException("Configure routes in Edit mode.");
        points = routePoints;
    }

    void Awake()
    {
        if (points.Length < 6 || points.Any(p => !p) || points.Distinct().Count() != points.Length)
        {
            Debug.LogError("Raid routes require at least six unique valid points.", this);
            enabled = false;
            return;
        }
        SpawnIndex = new System.Random(Guid.NewGuid().GetHashCode()).Next(points.Length);
        ExitIndices = SelectOppositeExits(points.Select(p => p.transform.position).ToArray(), SpawnIndex);
        for (int i = 0; i < points.Length; i++) points[i].SetRole(i == SpawnIndex, ExitIndices.Contains(i));
        Debug.Log($"RAID_ROUTE: entry={SpawnIndex + 1}; exits={string.Join(",", ExitIndices.Select(i => i + 1))}", this);
    }

    // Rank by direction across the map center, then distance. Nearby exits on the
    // entry side cannot outrank a point on the opposite side merely due to map shape.
    public static int[] SelectOppositeExits(IReadOnlyList<Vector3> positions, int spawnIndex)
    {
        if (positions == null || positions.Count < 4 || spawnIndex < 0 || spawnIndex >= positions.Count)
            throw new ArgumentException("At least four positions and a valid entry index are required.");
        Vector3 center = Vector3.zero;
        for (int i = 0; i < positions.Count; i++) center += positions[i];
        center /= positions.Count;
        Vector3 direction = center - positions[spawnIndex]; direction.y = 0; direction.Normalize();
        var candidates = Enumerable.Range(0, positions.Count).Where(i => i != spawnIndex);
        return candidates.OrderByDescending(i =>
        {
            var offset = positions[i] - center; offset.y = 0;
            return Vector3.Dot(offset.normalized, direction);
        }).ThenByDescending(i => (positions[i] - positions[spawnIndex]).sqrMagnitude).ThenBy(i => i).Take(3).ToArray();
    }

    void Start()
    {
        player = FindAnyObjectByType<TopDownPlayer>();
        if (player) inventory = player.GetComponent<PlayerInventory>();
    }

    void Update()
    {
        if (IsExtracting || !player) return;
        var health = player.GetComponent<RaidHealth>();
        RaidRoutePoint current = null;
        if (player.InputEnabled && (!health || health.health > 0) && (!inventory || !inventory.IsOpen))
            foreach (int index in ExitIndices)
                if (points[index].Contains(player.transform.position)) { current = points[index]; break; }
        if (current != CurrentExtraction) { CurrentExtraction = current; ExtractionProgress = 0; }
        if (!current) { ExtractionProgress = 0; return; }
        ExtractionProgress += Time.deltaTime;
        if (ExtractionProgress >= extractionSeconds) StartCoroutine(Extract());
    }

    IEnumerator Extract()
    {
        if (!Application.CanStreamedLevelBeLoaded(hideoutScene))
        {
            travelError = "Hideout scene is unavailable.";
            Debug.LogError(travelError, this);
            enabled = false;
            yield break;
        }
        IsExtracting = true;
        if (inventory) inventory.Close();
        player.InputEnabled = false;
        var operation = SceneManager.LoadSceneAsync(hideoutScene, LoadSceneMode.Single);
        if (operation == null)
        {
            IsExtracting = false; player.InputEnabled = true; ExtractionProgress = 0;
            yield break;
        }
        yield return operation;
    }

    void OnGUI()
    {
        if (!player || !SpawnPoint || (inventory && inventory.IsOpen)) return;
        if (heading == null)
        {
            heading = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
            label = new GUIStyle(GUI.skin.label) { fontSize = 13 };
        }
        float x = Screen.width - 278;
        GUI.Box(new Rect(x, 16, 262, 302), "");
        GUI.Label(new Rect(x + 12, 23, 242, 24), "RAID ROUTE  /  ENTRY " + (SpawnIndex + 1).ToString("00"), heading);
        DrawMap(new Rect(x + 48, 56, 166, 154));
        GUI.Label(new Rect(x + 12, 211, 242, 20), "GREEN: EXITS   /   BLUE: YOU", label);
        for (int i = 0; i < ExitIndices.Length; i++)
        {
            var point = points[ExitIndices[i]];
            float distance = Vector3.Distance(player.transform.position, point.transform.position);
            GUI.Label(new Rect(x + 12, 235 + i * 21, 242, 21), point.pointLabel + "  " + distance.ToString("F0") + "m", label);
        }
        var camera = Camera.main;
        if (camera)
            foreach (int index in ExitIndices)
            {
                var p = points[index];
                var screen = camera.WorldToScreenPoint(p.transform.position + Vector3.up * 2.6f);
                if (screen.z > 0 && screen.x > 70 && screen.x < Screen.width - 70 && screen.y > 50 && screen.y < Screen.height - 50)
                    GUI.Box(new Rect(screen.x - 75, Screen.height - screen.y - 15, 150, 28), "EXIT " + (index + 1).ToString("00"));
            }
        if (CurrentExtraction || IsExtracting || !string.IsNullOrEmpty(travelError))
        {
            string message = travelError ?? (IsExtracting ? "Returning to hideout..." :
                "EXTRACTING  " + Mathf.Max(0, extractionSeconds - ExtractionProgress).ToString("F1") + "s  /  Stay inside the green ring");
            GUI.Box(new Rect(Screen.width / 2f - 250, Screen.height - 150, 500, 48), message);
        }
    }

    void DrawMap(Rect rect)
    {
        float minX = points.Min(p => p.transform.position.x) - 4, maxX = points.Max(p => p.transform.position.x) + 4;
        float minZ = points.Min(p => p.transform.position.z) - 4, maxZ = points.Max(p => p.transform.position.z) + 4;
        GUI.Box(rect, "N");
        var previous = GUI.color;
        for (int i = 0; i < points.Length; i++)
        {
            Vector3 p = points[i].transform.position;
            Vector2 pixel = new Vector2(Mathf.Lerp(rect.x, rect.xMax, Mathf.InverseLerp(minX, maxX, p.x)),
                Mathf.Lerp(rect.yMax, rect.y, Mathf.InverseLerp(minZ, maxZ, p.z)));
            GUI.color = points[i].IsExtraction ? new Color(.4f, 1, .5f) : Color.gray;
            GUI.DrawTexture(new Rect(pixel.x - 4, pixel.y - 4, 8, 8), Texture2D.whiteTexture);
            GUI.Label(new Rect(pixel.x + 5, pixel.y - 9, 24, 20), (i + 1).ToString(), label);
        }
        Vector3 playerPosition = player.transform.position;
        float px = Mathf.Lerp(rect.x, rect.xMax, Mathf.InverseLerp(minX, maxX, playerPosition.x));
        float py = Mathf.Lerp(rect.yMax, rect.y, Mathf.InverseLerp(minZ, maxZ, playerPosition.z));
        GUI.color = Color.cyan;
        GUI.DrawTexture(new Rect(px - 3, py - 3, 6, 6), Texture2D.whiteTexture);
        GUI.color = previous;
    }
}
