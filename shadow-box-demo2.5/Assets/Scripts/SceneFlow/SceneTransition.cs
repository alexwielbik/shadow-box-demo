using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Fades to black, loads another scene, sends the player in through the named LevelDoor, and fades back in.
///
/// You never place this in a scene: the first call to Go() creates it (with its own black overlay canvas)
/// and it survives scene loads. Doors call SceneTransition.Go("Level_02", "Bottom").
/// </summary>
public class SceneTransition : MonoBehaviour
{
    private const float FadeOutTime = 0.35f;
    private const float FadeInTime = 0.45f;

    private static SceneTransition instance;

    /// <summary>True from the start of the fade-out until the fade-in has finished.</summary>
    public static bool IsBusy { get; private set; }

    /// <summary>The door the player should arrive at while a scene is loading (null otherwise).</summary>
    public static string PendingDoorId { get; private set; }

    private CanvasGroup fade;

    // Statics survive between Play sessions when Domain Reload is off
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        IsBusy = false;
        PendingDoorId = null;
    }

    /// <summary>Leave this scene for sceneName and arrive at the LevelDoor called doorId there.</summary>
    public static void Go(string sceneName, string doorId)
    {
        if (IsBusy) return;
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogWarning("SceneTransition.Go: no scene name given.");
            return;
        }
        Instance.StartCoroutine(Instance.Run(sceneName, doorId));
    }

    private static SceneTransition Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("SceneTransition");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<SceneTransition>();
                instance.BuildOverlay();
            }
            return instance;
        }
    }

    private void BuildOverlay()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000; // above every other UI

        fade = gameObject.AddComponent<CanvasGroup>();
        fade.alpha = 0f;
        fade.blocksRaycasts = false;
        fade.interactable = false;

        var image = new GameObject("Fade", typeof(RectTransform), typeof(Image));
        image.transform.SetParent(transform, false);
        var rt = (RectTransform)image.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        Image img = image.GetComponent<Image>();
        img.color = Color.black;
        img.raycastTarget = false;
    }

    private IEnumerator Run(string sceneName, string doorId)
    {
        IsBusy = true;

        yield return Fade(0f, 1f, FadeOutTime);

        // Switch states (and which objectives are done) belong to one room: save this room's,
        // then load the next room's saved state (or a fresh one) before its switches wake up
        string leavingScene = SceneManager.GetActiveScene().name;
        SwitchChannels.SaveRoom(leavingScene);
        SwitchChannels.EnterRoom(sceneName);

        PendingDoorId = doorId;
        AsyncOperation load = SceneManager.LoadSceneAsync(sceneName);
        if (load == null)
        {
            Debug.LogError($"SceneTransition: couldn't load '{sceneName}'. " +
                           "Is it spelled right and added to File > Build Profiles > Scene List?");
            PendingDoorId = null;
            SwitchChannels.EnterRoom(leavingScene);   // still here: put this room's state back
            yield return Fade(1f, 0f, FadeInTime);
            IsBusy = false;
            yield break;
        }
        while (!load.isDone) yield return null;

        // One frame so every Start() in the new scene has run (CameraFollow snaps in Start)
        yield return null;

        PlayerController player = FindAnyObjectByType<PlayerController>();
        LevelDoor door = LevelDoor.Find(doorId);
        if (player != null && door != null)
            door.BeginEntry(player);
        else if (!string.IsNullOrEmpty(doorId))
            Debug.LogWarning($"SceneTransition: no LevelDoor with Door Id '{doorId}' (or no player) in '{sceneName}'.");

        PendingDoorId = null;

        yield return Fade(1f, 0f, FadeInTime);
        IsBusy = false;
    }

    private IEnumerator Fade(float from, float to, float time)
    {
        for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
        {
            fade.alpha = Mathf.Lerp(from, to, t / time);
            yield return null;
        }
        fade.alpha = to;
    }
}