using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(BoxCollider))]
public sealed class ScenePortal : MonoBehaviour
{
    [SerializeField] string destinationScene = "Game";
    bool loading;
    void Reset() => GetComponent<BoxCollider>().isTrigger = true;
    void OnTriggerEnter(Collider other)
    {
        var player = other.GetComponentInParent<TopDownPlayer>();
        if (player && !loading) StartCoroutine(Travel(player));
    }
    IEnumerator Travel(TopDownPlayer player)
    {
        if (!Application.CanStreamedLevelBeLoaded(destinationScene))
        {
            Debug.LogError("Portal destination is not enabled in Build Settings: " + destinationScene, this);
            yield break;
        }
        loading = true;
        player.InputEnabled = false;
        var operation = SceneManager.LoadSceneAsync(destinationScene, LoadSceneMode.Single);
        if (operation == null) { loading = false; player.InputEnabled = true; yield break; }
        yield return operation;
    }
    void OnGUI()
    {
        var player = FindAnyObjectByType<PlayerInventory>();
        if (player && player.IsOpen) return;
        GUI.Box(new Rect(16, 16, 410, 52), loading
            ? "Loading forest..."
            : "WASD / Arrow keys: Move    Shift: Sprint\nEnter the blue portal in the front room to deploy.");
    }
}
