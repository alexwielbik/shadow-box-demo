using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A tiny particle system made of UI Images, for fire on a Screen Space - Overlay canvas
/// (regular Particle Systems don't draw on an Overlay canvas).
/// Spawns flames (and some embers) along this RectTransform's height; they rise, shrink and fade.
/// Particles live under Particle Layer, so they stay behind as this object moves along the paper.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class UIFlameEmitter : MonoBehaviour
{
    [Header("Look")]
    [Tooltip("Sprite for every particle. A plain circle is fine until there's flame art.")]
    [SerializeField] private Sprite particleSprite;
    [Tooltip("Where particles are created. Should not move with the emitter. Empty = this object's grandparent.")]
    [SerializeField] private RectTransform particleLayer;
    [SerializeField]
    private Gradient flameColor = MakeGradient(
        new Color(1f, 0.95f, 0.55f), new Color(1f, 0.55f, 0.1f), new Color(0.75f, 0.15f, 0.05f));
    [SerializeField]
    private Gradient emberColor = MakeGradient(
        new Color(1f, 0.8f, 0.3f), new Color(1f, 0.45f, 0.1f), new Color(0.4f, 0.1f, 0.05f));

    [Header("Flames")]
    [SerializeField, Min(0f)] private float flamesPerSecond = 45f;
    [SerializeField] private Vector2 flameLifetime = new Vector2(0.3f, 0.6f);
    [Tooltip("Start size in canvas pixels.")]
    [SerializeField] private Vector2 flameSize = new Vector2(12f, 26f);
    [Tooltip("Upward speed in canvas pixels per second.")]
    [SerializeField] private Vector2 flameRiseSpeed = new Vector2(40f, 90f);
    [Tooltip("Sideways speed (negative = drifts back over the ash).")]
    [SerializeField] private Vector2 flameSideSpeed = new Vector2(-30f, 5f);

    [Header("Embers")]
    [SerializeField, Range(0f, 1f)] private float emberChance = 0.15f;
    [SerializeField] private Vector2 emberLifetime = new Vector2(0.8f, 1.6f);
    [SerializeField] private Vector2 emberSize = new Vector2(4f, 8f);
    [SerializeField] private Vector2 emberRiseSpeed = new Vector2(30f, 70f);
    [SerializeField] private float emberWobble = 25f;

    [Header("Limits")]
    [SerializeField, Min(1)] private int maxParticles = 150;

    private class Particle
    {
        public RectTransform rt;
        public Image img;
        public Vector2 velocity;
        public float age, life, size, wobbleSeed;
        public bool ember;
    }

    private readonly List<Particle> live = new List<Particle>();
    private readonly Stack<Particle> pool = new Stack<Particle>();
    private RectTransform self;
    private bool emitting;
    private float intensity = 1f;
    private float spawnCarry;

    private void Awake()
    {
        self = (RectTransform)transform;
        if (particleLayer == null && self.parent != null)
            particleLayer = (self.parent.parent != null ? self.parent.parent : self.parent) as RectTransform;
    }

    private void OnDisable() => Clear();

    public void SetEmitting(bool on) => emitting = on;

    /// <summary>Multiplier on the spawn rate (1 = normal).</summary>
    public void SetIntensity(float multiplier) => intensity = Mathf.Max(0f, multiplier);

    /// <summary>Remove every particle right away.</summary>
    public void Clear()
    {
        for (int i = live.Count - 1; i >= 0; i--) Release(i);
        spawnCarry = 0f;
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        if (emitting && particleLayer != null)
        {
            spawnCarry += flamesPerSecond * intensity * dt;
            while (spawnCarry >= 1f)
            {
                spawnCarry -= 1f;
                Spawn();
            }
        }

        for (int i = live.Count - 1; i >= 0; i--)
        {
            Particle p = live[i];
            p.age += dt;
            if (p.age >= p.life) { Release(i); continue; }

            float t = p.age / p.life;
            Vector2 v = p.velocity;
            if (p.ember) v.x += Mathf.Sin((p.age + p.wobbleSeed) * 8f) * emberWobble;

            p.rt.localPosition += (Vector3)(v * dt);

            float size = p.ember ? p.size : p.size * Mathf.Lerp(1f, 0.25f, t);   // flames shrink as they rise
            p.rt.sizeDelta = new Vector2(size, size);
            p.img.color = (p.ember ? emberColor : flameColor).Evaluate(t);
        }
    }

    private void Spawn()
    {
        if (live.Count >= maxParticles) return;

        Particle p = pool.Count > 0 ? pool.Pop() : Create();
        p.rt.gameObject.SetActive(true);
        p.rt.SetAsLastSibling();

        // Random point along the emitter's height, converted into the particle layer's space
        Rect r = self.rect;
        Vector3 local = new Vector3(r.center.x, Random.Range(r.yMin, r.yMax), 0f);
        Vector3 world = self.TransformPoint(local);
        Vector3 inLayer = particleLayer.InverseTransformPoint(world);
        inLayer.z = 0f;
        p.rt.localPosition = inLayer;

        p.ember = Random.value < emberChance;
        p.age = 0f;
        p.wobbleSeed = Random.value * 10f;

        if (p.ember)
        {
            p.life = Random.Range(emberLifetime.x, emberLifetime.y);
            p.size = Random.Range(emberSize.x, emberSize.y);
            p.velocity = new Vector2(0f, Random.Range(emberRiseSpeed.x, emberRiseSpeed.y));
        }
        else
        {
            p.life = Random.Range(flameLifetime.x, flameLifetime.y);
            p.size = Random.Range(flameSize.x, flameSize.y);
            p.velocity = new Vector2(Random.Range(flameSideSpeed.x, flameSideSpeed.y),
                                     Random.Range(flameRiseSpeed.x, flameRiseSpeed.y));
        }

        p.rt.sizeDelta = new Vector2(p.size, p.size);
        p.img.color = (p.ember ? emberColor : flameColor).Evaluate(0f);
        live.Add(p);
    }

    private Particle Create()
    {
        var go = new GameObject("FlameParticle", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(particleLayer, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);

        var img = go.GetComponent<Image>();
        img.sprite = particleSprite;
        img.raycastTarget = false;

        return new Particle { rt = rt, img = img };
    }

    private void Release(int index)
    {
        Particle p = live[index];
        live.RemoveAt(index);
        if (p.rt != null)
        {
            p.rt.gameObject.SetActive(false);
            pool.Push(p);
        }
    }

    private static Gradient MakeGradient(Color start, Color mid, Color end)
    {
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(start, 0f), new GradientColorKey(mid, 0.4f), new GradientColorKey(end, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.5f), new GradientAlphaKey(0f, 1f) });
        return g;
    }
}